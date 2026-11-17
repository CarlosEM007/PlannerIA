using System.Windows.Controls;
using PlannerIA.ViewModels;

namespace PlannerIA.Views;

public partial class HojeView : UserControl
{
    public HojeView() => InitializeComponent();
}

public partial class SemanaView : UserControl
{
    public SemanaView() => InitializeComponent();
}

public partial class MesView : UserControl
{
    public MesView() => InitializeComponent();
}

public partial class VisaoGeralView : UserControl
{
    public VisaoGeralView() => InitializeComponent();
}

public partial class ConfiguracoesView : UserControl
{
    private ConfiguracoesViewModel? _vm;

    public ConfiguracoesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.LimparCampoChave -= Limpar;
            _vm = DataContext as ConfiguracoesViewModel;
            if (_vm is not null) _vm.LimparCampoChave += Limpar;
        };
        Unloaded += (_, _) => CampoChave.Clear(); // não deixa a chave digitada para trás ao trocar de tela
    }

    // PasswordBox não aceita binding na senha (por segurança); repassa ao ViewModel pelo evento.
    private void AoMudarChave(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm is not null) _vm.NovaChave = CampoChave.Password;
    }

    private void Limpar(object? sender, EventArgs e) => CampoChave.Clear();
}
