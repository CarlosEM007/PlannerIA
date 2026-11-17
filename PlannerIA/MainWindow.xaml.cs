using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using PlannerIA.ViewModels;

namespace PlannerIA;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly BlurEffect _desfoque = new() { Radius = 0, RenderingBias = RenderingBias.Performance };
    private static readonly IEasingFunction Suave = new CubicEase { EasingMode = EasingMode.EaseOut };

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += AoMudarViewModel;

        Loaded += (_, _) =>
        {
            MoverIndicador(animar: false);
            Keyboard.Focus(this); // garante que os atalhos funcionem logo ao abrir
        };
        StateChanged += (_, _) => AjustarMaximizado();
        SourceInitialized += (_, _) => ArredondarCantos();
        PreviewKeyDown += AoPressionarTecla;
    }

    // ---------- janela ----------

    private void Minimizar(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximizar(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Fechar(object sender, RoutedEventArgs e) => Close();

    private void AjustarMaximizado()
    {
        // Com WindowChrome, a janela maximizada passa um pouco da tela; compensa com margem.
        Raiz.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        BotaoMaximizar.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ArredondarCantos()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preferencia = 2; // DWMWCP_ROUND (Windows 11)
            DwmSetWindowAttribute(hwnd, 33, ref preferencia, sizeof(int));
        }
        catch
        {
            // Windows 10: sem cantos arredondados.
        }
    }

    // ---------- navegação ----------

    private void AoSelecionarNav(object sender, RoutedEventArgs e) => MoverIndicador(animar: IsLoaded);

    private void MoverIndicador(bool animar)
    {
        var selecionado = Nav.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (selecionado is null || !Nav.IsLoaded) return;

        var y = selecionado.TranslatePoint(new Point(0, 0), Nav).Y;
        Indicador.Height = selecionado.ActualHeight;
        Indicador.Width = Nav.ActualWidth;

        if (!animar)
        {
            IndicadorPos.Y = y;
            Indicador.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
            return;
        }

        IndicadorPos.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } });
    }

    // ---------- diálogo ----------

    private void AoMudarViewModel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Dialogo))
        {
            if (_vm.Dialogo is not null) MostrarDialogo(_vm.Dialogo);
            else EsconderDialogo();
        }
        else if (e.PropertyName == nameof(MainViewModel.PaginaAtual))
        {
            Dispatcher.BeginInvoke(() => MoverIndicador(animar: true));
        }
    }

    private void MostrarDialogo(DialogoViewModel dialogo)
    {
        ConteudoDialogo.Content = dialogo;
        CamadaDialogo.Visibility = Visibility.Visible;
        Principal.Effect = _desfoque;

        var dur = TimeSpan.FromMilliseconds(260);
        CamadaDialogo.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = Suave });
        EscalaDialogo.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
        EscalaDialogo.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
        _desfoque.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(8, dur) { EasingFunction = Suave });
    }

    private void EsconderDialogo()
    {
        var dur = TimeSpan.FromMilliseconds(180);
        var saida = new DoubleAnimation(0, dur) { EasingFunction = Suave };
        saida.Completed += (_, _) =>
        {
            if (_vm.Dialogo is not null) return; // outro diálogo abriu nesse meio-tempo
            CamadaDialogo.Visibility = Visibility.Collapsed;
            ConteudoDialogo.Content = null;
            Principal.Effect = null;
        };
        CamadaDialogo.BeginAnimation(OpacityProperty, saida);
        EscalaDialogo.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, dur));
        EscalaDialogo.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, dur));
        _desfoque.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, dur));
    }

    private void AoClicarFundoDialogo(object sender, MouseButtonEventArgs e) => _vm.Dialogo?.CancelarCommand.Execute(null);

    private void AoPressionarTecla(object sender, KeyEventArgs e)
    {
        if (_vm.Dialogo is not { } dlg) return;

        if (e.Key == Key.Escape)
        {
            dlg.CancelarCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox { Tag: "EmLinha" } emLinha)
        {
            // Edição em linha (prévia da IA): Enter só confirma o campo, não o diálogo.
            emLinha.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            emLinha.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !(Keyboard.FocusedElement is TextBox { AcceptsReturn: true } && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            // Atualiza o binding do campo com foco antes de confirmar.
            if (Keyboard.FocusedElement is TextBox tb)
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            dlg.ConfirmarCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && !CartaoDialogo.IsKeyboardFocusWithin)
        {
            CartaoDialogo.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            e.Handled = true;
        }
    }
}
