using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Somning.Platform.Windows.Update;

/// <summary>Native message boxes and the installer launcher used by the updater. Boxes block the calling thread.</summary>
public static class UpdateDialogs
{
    private const uint MbYesNo = 0x4;
    private const uint MbOk = 0x0;
    private const uint MbIconInformation = 0x40;
    private const uint MbIconQuestion = 0x20;
    private const uint MbIconError = 0x10;
    private const uint MbSetForeground = 0x10000;
    private const uint MbTopMost = 0x40000;
    private const int IdYes = 6;
    private const string Title = "Somning";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);

    /// <summary>Asks whether to install the new version now. "Não" leaves the current version running.</summary>
    public static bool AskToUpdate(Version current, Version latest) =>
        MessageBox(0,
            $"Há uma nova versão do Somning: {Format(latest)} (você está na {Format(current)}).\n\n" +
            "Atualizar agora? O Somning será baixado do GitHub, fechado, atualizado e aberto de novo. Suas configurações são mantidas.",
            Title, MbYesNo | MbIconQuestion | MbSetForeground | MbTopMost) == IdYes;

    public static void ShowUpToDate(Version current) =>
        MessageBox(0, $"Você já está na versão mais recente ({Format(current)}).", Title, MbOk | MbIconInformation | MbSetForeground | MbTopMost);

    public static void ShowFailure(string reason) =>
        MessageBox(0, "Não foi possível atualizar o Somning agora.\n\n" + reason + "\n\nTente de novo mais tarde pelo menu da bandeja ou baixe a versão nova em github.com/JotaEfi/somning/releases.",
            Title, MbOk | MbIconError | MbSetForeground | MbTopMost);

    /// <summary>
    /// Starts the verified installer silently. It runs through <c>cmd /c start</c> so it is not a child of this process:
    /// the installer stops the running app (and its process tree) before replacing files, which must not kill itself.
    /// </summary>
    public static void LaunchInstaller(string installerPath, bool modelsAlreadyInstalled)
    {
        var arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1" + (modelsAlreadyInstalled ? " /SEMMODELOS=1" : string.Empty);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{installerPath}\" {arguments}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }

    private static string Format(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";
}
