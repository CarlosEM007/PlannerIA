using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PlannerIA.Controles;

/// <summary>ContentControl que faz fade + deslize ao trocar de conteúdo (troca de página).</summary>
public class TransicaoContentControl : ContentControl
{
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Animacoes.Entrar(this, deslocamento: 18, duracaoMs: 340);
    }
}

public static class Animacoes
{
    private static readonly IEasingFunction Suave = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void Entrar(UIElement el, double deslocamento = 14, int duracaoMs = 320, int atrasoMs = 0)
    {
        var tt = el.RenderTransform as TranslateTransform;
        if (tt is null || tt.IsFrozen)
        {
            tt = new TranslateTransform();
            el.RenderTransform = tt;
        }

        var atraso = TimeSpan.FromMilliseconds(atrasoMs);
        var dur = TimeSpan.FromMilliseconds(duracaoMs);

        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, dur) { BeginTime = atraso, EasingFunction = Suave });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(deslocamento, 0, dur) { BeginTime = atraso, EasingFunction = Suave });
    }

    // ---- Propriedade anexada: anima a entrada dos itens de uma lista em cascata ----

    public static readonly DependencyProperty EntradaEmCascataProperty = DependencyProperty.RegisterAttached(
        "EntradaEmCascata", typeof(bool), typeof(Animacoes), new PropertyMetadata(false, AoMudarCascata));

    public static bool GetEntradaEmCascata(DependencyObject d) => (bool)d.GetValue(EntradaEmCascataProperty);
    public static void SetEntradaEmCascata(DependencyObject d, bool v) => d.SetValue(EntradaEmCascataProperty, v);

    private static void AoMudarCascata(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || e.NewValue is not true) return;
        fe.Loaded += (_, _) =>
        {
            var indice = 0;
            DependencyObject? atual = fe;
            for (var nivel = 0; nivel < 4 && atual is not null; nivel++)
            {
                if (ItemsControl.ItemsControlFromItemContainer(atual) is { } ic)
                {
                    indice = ic.ItemContainerGenerator.IndexFromContainer(atual);
                    break;
                }
                atual = VisualTreeHelper.GetParent(atual);
            }
            Entrar(fe, deslocamento: 12, duracaoMs: 360, atrasoMs: Math.Min(Math.Max(indice, 0), 12) * 35);
        };
    }
}

/// <summary>Coloca o foco do teclado no elemento assim que ele aparece (ex.: primeiro campo de um diálogo).</summary>
public static class Foco
{
    public static readonly DependencyProperty AoCarregarProperty = DependencyProperty.RegisterAttached(
        "AoCarregar", typeof(bool), typeof(Foco), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not FrameworkElement fe || e.NewValue is not true) return;
            fe.Loaded += (_, _) => fe.Dispatcher.BeginInvoke(() =>
            {
                fe.Focus();
                System.Windows.Input.Keyboard.Focus(fe);
                if (fe is TextBox tb) tb.CaretIndex = tb.Text.Length;
            }, System.Windows.Threading.DispatcherPriority.Input);
        }));

    public static bool GetAoCarregar(DependencyObject d) => (bool)d.GetValue(AoCarregarProperty);
    public static void SetAoCarregar(DependencyObject d, bool v) => d.SetValue(AoCarregarProperty, v);
}
