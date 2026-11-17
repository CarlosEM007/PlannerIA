using PlannerIA.Core.Portas;
using PlannerIA.Infrastructure.Ia;

namespace PlannerIA.Tests;

/// <summary>
/// Testa o PowerShell de verdade, mas com uma variável temporária própria do teste
/// (nunca a ANTHROPIC_API_KEY). A variável é apagada no fim.
/// </summary>
public class ChaveApiTests
{
    // Limpeza direta no registro: Environment.SetEnvironmentVariable(..., User) espera todas as janelas responderem e é lento.
    private static void ApagarDoRegistro(string nome)
    {
        if (OperatingSystem.IsWindows())
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment", writable: true)?.DeleteValue(nome, throwOnMissingValue: false);
    }

    [Fact]
    public async Task Salva_le_e_remove_a_variavel_do_usuario_via_powershell()
    {
        if (!OperatingSystem.IsWindows()) return;

        var nome = $"PLANNERIA_TESTE_{Guid.NewGuid():N}";
        var armazenamento = new ChaveApiPowerShell(nome);
        try
        {
            await armazenamento.SalvarAsync("sk-ant-teste-1234567890");

            Assert.Equal("sk-ant-teste-1234567890", Environment.GetEnvironmentVariable(nome, EnvironmentVariableTarget.User));
            Assert.Equal("sk-ant-teste-1234567890", Environment.GetEnvironmentVariable(nome)); // processo atual já enxerga
            Assert.Equal(("sk-ant-teste-1234567890", OrigemChave.Usuario), armazenamento.Ler());

            await armazenamento.RemoverAsync();

            Assert.Null(Environment.GetEnvironmentVariable(nome, EnvironmentVariableTarget.User));
            Assert.Equal(((string?)null, OrigemChave.Nenhuma), armazenamento.Ler());
        }
        finally
        {
            ApagarDoRegistro(nome);
            Environment.SetEnvironmentVariable(nome, null);
        }
    }

    [Fact]
    public async Task Chave_com_caracteres_especiais_chega_intacta()
    {
        if (!OperatingSystem.IsWindows()) return;

        var nome = $"PLANNERIA_TESTE_{Guid.NewGuid():N}";
        var armazenamento = new ChaveApiPowerShell(nome);
        const string chave = "sk-ant-a'b\"c$d`e;f|g&h";
        try
        {
            await armazenamento.SalvarAsync(chave);
            Assert.Equal(chave, Environment.GetEnvironmentVariable(nome, EnvironmentVariableTarget.User));
        }
        finally
        {
            ApagarDoRegistro(nome);
            Environment.SetEnvironmentVariable(nome, null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sk-ant-com espaco")]
    public async Task Chave_vazia_ou_com_espacos_e_recusada_sem_rodar_o_powershell(string chave)
    {
        var nome = $"PLANNERIA_TESTE_{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<FalhaIaException>(() => new ChaveApiPowerShell(nome).SalvarAsync(chave));
        Assert.Null(Environment.GetEnvironmentVariable(nome));
    }
}
