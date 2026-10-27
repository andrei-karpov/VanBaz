# Original VanBaz vector mark. Generate ICO PNG frames at every shell/tray size.
Add-Type -AssemblyName System.Drawing
$assetFolder=Join-Path $PSScriptRoot '..\src\VanBaz\Assets'
$frames=@()
foreach($size in @(16,20,24,32,40,48,64,128,256)) {
 $bitmap=New-Object System.Drawing.Bitmap ($size*4),($size*4)
 $g=[System.Drawing.Graphics]::FromImage($bitmap)
 $g.SmoothingMode='AntiAlias'; $g.ScaleTransform($size*4/256,$size*4/256)
 $path=New-Object System.Drawing.Drawing2D.GraphicsPath
 foreach($arc in @(@(8,8,72,72,180,90),@(176,8,72,72,270,90),@(176,176,72,72,0,90),@(8,176,72,72,90,90))) { $path.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],$arc[5]) }; $path.CloseFigure()
 $blue=New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#147DB0'))
 $gold=New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#FFD05A'))
 $g.FillPath($blue,$path)
 $points=[System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 64,51),(New-Object System.Drawing.PointF 211,128),(New-Object System.Drawing.PointF 64,205),(New-Object System.Drawing.PointF 93,128))
 $g.FillPolygon([System.Drawing.Brushes]::White,$points)
 $g.FillEllipse($gold,24,112,32,32)
 $small=New-Object System.Drawing.Bitmap $size,$size
 $sg=[System.Drawing.Graphics]::FromImage($small); $sg.InterpolationMode='HighQualityBicubic';$sg.DrawImage($bitmap,0,0,$size,$size)
 $ms=New-Object System.IO.MemoryStream; $small.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
 $frames+=,@{Size=$size;Data=$ms.ToArray()}
 $ms.Dispose();$sg.Dispose();$small.Dispose();$g.Dispose();$bitmap.Dispose();$path.Dispose();$blue.Dispose();$gold.Dispose()
}
$file=[IO.File]::Create((Join-Path $assetFolder 'VanBaz.ico'));$writer=New-Object IO.BinaryWriter $file
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
$offset=6+16*$frames.Count
foreach($frame in $frames) { $dim=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$dim);$writer.Write([byte]$dim);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Data.Length);$writer.Write([uint32]$offset);$offset+=$frame.Data.Length }
foreach($frame in $frames){$writer.Write([byte[]]$frame.Data)};$writer.Dispose();$file.Dispose()
