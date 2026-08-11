# Abre o app servindo a interface do disco e fotografa a janela.
#
# Usa PrintWindow, e não CopyFromScreen: assim a captura sai do conteúdo da
# própria janela, mesmo que ela esteja atrás de outras. Sem isso a foto vira o
# que estiver por cima — e trazer a janela para frente roubaria o foco de quem
# estiver trabalhando na máquina.
param([string]$Saida = 'C:\Users\andre\MeetingApp\ui.png', [int]$Espera = 9,
      [string]$Tela = '',
      [string]$Gravacoes = 'C:\Users\andre\Documents\MeetingRecordings')
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class V {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  public struct RECT { public int L, T, R, B; }
}
'@
Get-Process MeetingApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$web = '\\wsl$\Ubuntu\home\andre\projects\meeting-transcription\app-net\App\web'
$p = Start-Process 'C:\Users\andre\MeetingApp\MeetingApp.exe' -PassThru `
     -ArgumentList (@('--web',$web,'--gravacoes',$Gravacoes) + $(if ($Tela) { @('--tela',$Tela) } else { @() }))
Start-Sleep -Seconds $Espera
if ($p.HasExited) { "MORREU: " + $p.ExitCode; exit 1 }
$h = [V]::FindWindowW('MeetingApp.Janela', [NullString]::Value)
if ($h -eq [IntPtr]::Zero) { 'janela nao encontrada'; exit 1 }
$r = New-Object V+RECT; [void][V]::GetWindowRect($h, [ref]$r)
$bmp = New-Object Drawing.Bitmap ($r.R-$r.L), ($r.B-$r.T)
$g = [Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT, necessário para conteúdo acelerado como o WebView2
[void][V]::PrintWindow($h, $dc, 2)
$g.ReleaseHdc($dc)
$bmp.Save($Saida, [Drawing.Imaging.ImageFormat]::Png)
"ok: $Saida"
