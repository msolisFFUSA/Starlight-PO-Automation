[CmdletBinding()]
param([Parameter(Mandatory)][string]$PdfPath)

$ErrorActionPreference = 'Stop'
$popplerCandidates = @(@(
  $env:POPPLER_EXE,
  (Join-Path $PSScriptRoot 'tools\pdftoppm.exe'),
  'C:\Program Files\poppler\Library\bin\pdftoppm.exe',
  'C:\Program Files\poppler\bin\pdftoppm.exe'
) | Where-Object {$_ -and (Test-Path -LiteralPath $_)})
if($popplerCandidates.Count -gt 0){$poppler=$popplerCandidates[0]}else{$command=Get-Command pdftoppm.exe -ErrorAction SilentlyContinue;if(-not $command){throw 'pdftoppm.exe was not found. Install Poppler, add it to PATH, or set POPPLER_EXE.'};$poppler=$command.Source}

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrEngine,Windows.Media.Ocr,ContentType=WindowsRuntime]
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1} | Select-Object -First 1
function Await-WinRt($operation,[type]$type){$task=$asTask.MakeGenericMethod($type).Invoke($null,@($operation));$task.GetAwaiter().GetResult()}
function Get-Words([string]$imagePath){
  $file=Await-WinRt ([Windows.Storage.StorageFile]::GetFileFromPathAsync($imagePath)) ([Windows.Storage.StorageFile])
  $stream=Await-WinRt ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  try{
    $decoder=Await-WinRt ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap=Await-WinRt ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    try{
      $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
      $result=Await-WinRt ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
      @($result.Lines | ForEach-Object {$_.Words | ForEach-Object {[pscustomobject]@{text=$_.Text;x=[double]$_.BoundingRect.X;y=[double]$_.BoundingRect.Y}}})
    }finally{$bitmap.Dispose()}
  }finally{$stream.Dispose()}
}
function Parse-Page([object[]]$words,[int]$page){
  $quantityHeader=$words | Where-Object {$_.text -match '^(?i:Quantity)$'} | Sort-Object y | Select-Object -First 1
  if(-not $quantityHeader){
    $firstTotal=$words | Where-Object {$_.text -match '^(?i:Total):?$'} | Sort-Object x,y | Select-Object -First 1
    if(-not $firstTotal){return @()}
    $quantityHeader=[pscustomobject]@{x=$firstTotal.x;y=0}
  }
  $rows=[System.Collections.Generic.List[object]]::new();$seen=[System.Collections.Generic.HashSet[int]]::new()
  foreach($seed in $words | Where-Object {$_.x -lt ($quantityHeader.x-150) -and $_.text.Contains('(')}){
    $key=[int][Math]::Floor($seed.y/40);if(-not $seen.Add($key)){continue}
    $line=@($words | Where-Object {[Math]::Abs($_.y-$seed.y)-lt 21 -and $_.x -lt ($quantityHeader.x-150)} | Sort-Object x)
    $lineText=(($line|ForEach-Object text)-join ' ')-replace '\s+',' '
    if($lineText -match '(?i)Takeoff|Vendor|Baseplan|Finish Out|Project|Elevation'){continue}
    $match=[regex]::Match($lineText,'\((.+?)\)')
    if(-not $match.Success){
      $continuation=@($words | Where-Object {$_.y -gt ($seed.y+21) -and $_.y -le ($seed.y+85) -and $_.x -lt ($quantityHeader.x-150)} | Sort-Object y,x)
      $lineText=((@($line)+@($continuation)|ForEach-Object text)-join ' ')-replace '\s+',' '
      $match=[regex]::Match($lineText,'\((.+?)\)')
    }
    if(-not $match.Success){continue}
    $description=$match.Groups[1].Value.Trim()
    if($description -match '(?i)\bEach\b|\bTotal\b'){continue}
    $total=$words | Where-Object {$_.x -gt ($quantityHeader.x-80) -and [Math]::Abs($_.y-$seed.y)-lt 55 -and $_.text -match '^(?i:Total):?$'} | Sort-Object {[Math]::Abs($_.y-$seed.y)} | Select-Object -First 1
    $value=$null;if($total){$value=$words | Where-Object {$_.x -gt $total.x -and $_.x -lt ($total.x+180) -and [Math]::Abs($_.y-$total.y)-lt 26 -and ($_.text -match '^\d+$' -or $_.text -match '^(?i:o)$')} | Sort-Object x | Select-Object -First 1}
    if($value){$quantity=if($value.text -match '^(?i:o)$'){0}else{[int]$value.text};$rows.Add([pscustomobject]@{description=$description;quantity=$quantity;page=$page;position=$seed.y})}
  }
  @($rows)
}
function Read-Items([string]$renderPrefix,[int]$dpi){
  & $poppler -r $dpi -png $PdfPath (Join-Path $temp $renderPrefix)
  if($LASTEXITCODE -ne 0){throw 'PDF rendering failed.'}
  $items=[System.Collections.Generic.List[object]]::new()
  $pages=Get-ChildItem -LiteralPath $temp -Filter ($renderPrefix+'-*.png') | Sort-Object Name
  for($i=0;$i-lt$pages.Count;$i++){
    Parse-Page (Get-Words $pages[$i].FullName) ($i+1) | ForEach-Object {$_ | Add-Member -NotePropertyName sourceDpi -NotePropertyValue $dpi;$items.Add($_)}
  }
  [pscustomobject]@{items=$items;pages=$pages.Count}
}

$temp=Join-Path ([IO.Path]::GetTempPath()) ('po-reader-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $temp|Out-Null
try{
  $primary=Read-Items 'page' 300
  $recovery=Read-Items 'recovery' 150
  foreach($candidate in $recovery.items){
    $nearest=$primary.items | Where-Object {$_.page -eq $candidate.page} | ForEach-Object {[Math]::Abs(($_.position/$_.sourceDpi)-($candidate.position/$candidate.sourceDpi))} | Sort-Object | Select-Object -First 1
    if($null -eq $nearest -or $nearest -gt 0.6){$primary.items.Add($candidate)}
  }
  $publicItems=@($primary.items | Sort-Object page,@{Expression={$_.position/$_.sourceDpi}} | Select-Object description,quantity,page)
  [pscustomobject]@{items=$publicItems;totalQuantity=@($publicItems|Measure-Object quantity -Sum).Sum;pages=$primary.pages}|ConvertTo-Json -Depth 4 -Compress
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
