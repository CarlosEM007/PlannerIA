using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;
using PlannerIA.Infrastructure.Dados;
using PlannerIA.Infrastructure.Ia;
using PlannerIA.Infrastructure.Importacao;
using PlannerIA.Servicos;
using PlannerIA.ViewModels;

namespace PlannerIA;

public partial class App : Application
{
    private ServiceProvider? _servicos;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CultureInfo.DefaultThreadCurrentCulture = Datas.PtBr;
        CultureInfo.DefaultThreadCurrentUICulture = Datas.PtBr;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Datas.PtBr.IetfLanguageTag)));

        DispatcherUnhandledException += AoErroNaoTratado;

        var config = ConfiguracaoApp.Carregar();
        GerenciadorTema.Aplicar(config.Tema);

        var servicos = new ServiceCollection();
        servicos.AddSingleton(config);
        servicos.AddSingleton<IPlanoRepository>(_ => PlanoRepository.CriarParaArquivo(Caminhos.Banco));
        servicos.AddSingleton<IImportadorPlanilha>(_ => new ImportadorPlanilhaXlsx());
        servicos.AddSingleton<PlanoService>();
        servicos.AddSingleton(_ => new ClienteClaudeCli(Path.Combine(Caminhos.PastaDados, "claude-cli")));
        servicos.AddSingleton<ClienteClaude>();
        if (Environment.GetEnvironmentVariable("PLANNERIA_IA_DEMO") == "1")
            servicos.AddSingleton<IClienteIa, ClienteIaDemo>(); // simula a IA para testar a interface
        else
            // Claude CLI (assinatura) ou API (chave), conforme a escolha em Configurações.
            servicos.AddSingleton<IClienteIa>(sp => new ClienteIaRoteador(
                sp.GetRequiredService<ClienteClaudeCli>(),
                sp.GetRequiredService<ClienteClaude>(),
                () => config.LerModoIa()));
        servicos.AddSingleton<GeradorDePlano>();
        servicos.AddSingleton<IArmazenamentoChaveApi>(_ => new ChaveApiPowerShell());
        servicos.AddSingleton<MainViewModel>();
        servicos.AddSingleton<MainWindow>();
        _servicos = servicos.BuildServiceProvider();

        var janela = _servicos.GetRequiredService<MainWindow>();
        MainWindow = janela;
        janela.Show();

        var vm = _servicos.GetRequiredService<MainViewModel>();
        try
        {
            await vm.InicializarAsync();
            vm.IniciarMonitorIa(); // IA é opcional: sem chave ou sem internet o app funciona igual

            // Atalho de desenvolvimento: PlannerIA.exe --importar "planilha.xlsx"
            var i = Array.IndexOf(e.Args, "--importar");
            if (i >= 0 && i + 1 < e.Args.Length)
                await vm.ImportarArquivoAsync(e.Args[i + 1]);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o banco local:\n{ex.Message}", "PlannerIA",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AoErroNaoTratado(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_servicos?.GetService<MainViewModel>() is { } vm)
        {
            vm.Avisar($"Erro inesperado: {e.Exception.Message}", "", erro: true);
            e.Handled = true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _servicos?.Dispose();
        base.OnExit(e);
    }
}
