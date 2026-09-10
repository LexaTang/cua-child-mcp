param([string]$Source = "$PSScriptRoot/../assets/icon.png", [string]$Destination = "$PSScriptRoot/../assets/app.ico")
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$original=[Drawing.Image]::FromFile([IO.Path]::GetFullPath($Source))
$frames=[Collections.Generic.List[byte[]]]::new()
$sizes=@(16,20,24,32,40,48,64,128,256)
try {
 foreach($size in $sizes){
  $bmp=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g=[Drawing.Graphics]::FromImage($bmp)
  $ms=[IO.MemoryStream]::new()
  try {
   $g.Clear([Drawing.Color]::Transparent)
   $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
   $g.DrawImage($original,[Drawing.Rectangle]::new(0,0,$size,$size))
   $bmp.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
   $frames.Add($ms.ToArray())
  } finally {$ms.Dispose();$g.Dispose();$bmp.Dispose()}
 }
 $stream=[IO.File]::Create([IO.Path]::GetFullPath($Destination))
 $writer=[IO.BinaryWriter]::new($stream)
 try {
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
  $offset=6+16*$sizes.Count
  for($i=0;$i -lt $sizes.Count;$i++){
   $dim=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
   $writer.Write([byte]$dim);$writer.Write([byte]$dim);$writer.Write([byte]0);$writer.Write([byte]0)
   $writer.Write([uint16]1);$writer.Write([uint16]32)
   $writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
   $offset+=$frames[$i].Length
  }
  foreach($frame in $frames){$writer.Write($frame)}
 } finally {$writer.Dispose()}
} finally {$original.Dispose()}
Write-Host "Created ICO with $($sizes.Count) resolutions."
