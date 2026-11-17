using System.Diagnostics;
using System.Runtime.InteropServices;
using PlannerIA.Core.Portas;

namespace PlannerIA.Infrastructure.Ia;

/// <summary>
/// Salva a chave da API como variável de ambiente do usuário rodando um comando do PowerShell:
/// <c>New-ItemProperty -Path 'HKCU:\Environment' ...</c> (variável de ambiente do usuário).
/// A chave é passada ao PowerShell por uma variável de ambiente do processo filho, nunca pela
/// linha de comando, para não aparecer em listas de processos nem em logs de comandos.
/// </summary>
public sealed class ChaveApiPowerShell(string nomeVariavel = ClienteClaude.VariavelChave) : IArmazenamentoChaveApi
{
    private const string VariavelTransporte = "PLANNERIA_NOVA_CHAVE";

    public string NomeVariavel { get; } = nomeVariavel;

    public (string? Chave, OrigemChave Origem) Ler()
    {
        var usuario = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable(NomeVariavel, EnvironmentVariableTarget.User)
            : null;
        var processo = Environment.GetEnvironmentVariable(NomeVariavel);

        if (!string.IsNullOrWhiteSpace(processo) && processo != usuario) return (processo.Trim(), OrigemChave.Outra);
        if (!string.IsNullOrWhiteSpace(usuario)) return (usuario.Trim(), OrigemChave.Usuario);
        return (null, OrigemChave.Nenhuma);
    }

    public async Task SalvarAsync(string chave, CancellationToken ct = default)
    {
        chave = chave?.Trim() ?? "";
        if (chave.Length == 0)
            throw new FalhaIaException("Cole a chave da API antes de salvar.", podeTentarDeNovo: false);
        if (chave.Any(char.IsWhiteSpace))
            throw new FalhaIaException("A chave não pode ter espaços ou quebras de linha.", podeTentarDeNovo: false);

        await ExecutarAsync(chave, ct);
        // O próprio app passa a usar a chave nova na hora, sem reiniciar.
        Environment.SetEnvironmentVariable(NomeVariavel, chave);
    }

    public async Task RemoverAsync(CancellationToken ct = default)
    {
        await ExecutarAsync("", ct);
        Environment.SetEnvironmentVariable(NomeVariavel, null);
    }

    private async Task ExecutarAsync(string valor, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new FalhaIaException("Salvar a chave pelo app só funciona no Windows.", false);

        // Grava direto em HKCU:\Environment (onde ficam as variáveis do usuário). Não usamos
        // [Environment]::SetEnvironmentVariable(..., 'User') porque ele espera cada janela aberta
        // responder ao aviso de mudança, o que pode levar mais de um minuto. O aviso é enviado
        // depois, sem esperar (AvisarJanelas). Nome fixo no script; o valor chega por $env.
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"$v = $env:{VariavelTransporte}; " +
            "if ([string]::IsNullOrEmpty($v)) { " +
            $"Remove-ItemProperty -Path 'HKCU:\\Environment' -Name '{NomeVariavel}' -ErrorAction SilentlyContinue " +
            "} else { " +
            $"New-ItemProperty -Path 'HKCU:\\Environment' -Name '{NomeVariavel}' -Value $v -PropertyType String -Force | Out-Null " +
            "}";

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script })
            psi.ArgumentList.Add(a);
        psi.Environment[VariavelTransporte] = valor;

        Process? processo;
        try
        {
            processo = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new FalhaIaException("Não foi possível abrir o PowerShell para salvar a chave.", true, ex);
        }
        if (processo is null)
            throw new FalhaIaException("Não foi possível abrir o PowerShell para salvar a chave.", true);

        using (processo)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(30));
            var erro = processo.StandardError.ReadToEndAsync(limite.Token);
            try
            {
                await processo.WaitForExitAsync(limite.Token);
            }
            catch (OperationCanceledException)
            {
                try { processo.Kill(entireProcessTree: true); } catch { /* já terminou */ }
                if (ct.IsCancellationRequested) throw;
                throw new FalhaIaException("O PowerShell demorou demais para responder.", true);
            }

            if (processo.ExitCode != 0)
            {
                var detalhe = (await erro).Trim();
                throw new FalhaIaException(
                    "O PowerShell não conseguiu gravar a variável" + (detalhe.Length > 0 ? $": {detalhe}" : "."), true);
            }
        }

        AvisarJanelas();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam);

    /// <summary>
    /// Avisa o Windows (Explorer etc.) que as variáveis mudaram, para que programas abertos depois
    /// recebam o valor novo. SendNotifyMessage não espera resposta, então nunca trava.
    /// </summary>
    private static void AvisarJanelas()
    {
        const uint WmSettingChange = 0x001A;
        try
        {
            SendNotifyMessage(new IntPtr(0xFFFF), WmSettingChange, UIntPtr.Zero, "Environment");
        }
        catch
        {
            // Sem o aviso, só programas abertos depois de um novo login veem a variável; o PlannerIA já usa.
        }
    }
}
