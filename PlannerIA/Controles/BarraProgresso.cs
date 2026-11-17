using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PlannerIA.Controles;

/// <summary>Barra de progresso arredondada que anima suavemente quando o valor muda.</summary>
public class BarraProgresso : FrameworkElement
{
    public static readonly DependencyProperty ValorProperty = DependencyProperty.Register(
        nameof(Valor), typeof(double), typeof(BarraProgresso),
        new PropertyMetadata(0d, (d, e) => ((BarraProgresso)d).Animar((double)e.NewValue)));

    private static readonly DependencyProperty ValorAnimadoProperty = DependencyProperty.Register(
        "ValorAnimado", typeof(double), typeof(BarraProgresso),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrilhoProperty = DependencyProperty.Register(
        nameof(Trilho), typeof(Brush), typeof(BarraProgresso),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PreenchimentoProperty = DependencyProperty.Register(
        nameof(Preenchimento), typeof(Brush), typeof(BarraProgresso),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Valor { get => (double)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public Brush Trilho { get => (Brush)GetValue(TrilhoProperty); set => SetValue(TrilhoProperty, value); }
    public Brush Preenchimento { get => (Brush)GetValue(PreenchimentoProperty); set => SetValue(PreenchimentoProperty, value); }

    public BarraProgresso()
    {
        Height = 6;
        SnapsToDevicePixels = true;
        Loaded += (_, _) => Animar(Valor, inicial: true);
    }

    private void Animar(double alvo, bool inicial = false)
    {
        alvo = Math.Clamp(double.IsNaN(alvo) ? 0 : alvo, 0, 1);
        if (!IsLoaded && !inicial) { SetValue(ValorAnimadoProperty, alvo); return; }

        var anim = new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(inicial ? 900 : 550))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (inicial) SetValue(ValorAnimadoProperty, 0d);
        BeginAnimation(ValorAnimadoProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var r = h / 2;
        dc.DrawRoundedRectangle(Trilho, null, new Rect(0, 0, w, h), r, r);

        var v = (double)GetValue(ValorAnimadoProperty);
        if (v <= 0.0001) return;
        var fw = Math.Max(h, w * v);
        dc.DrawRoundedRectangle(Preenchimento, null, new Rect(0, 0, fw, h), r, r);
    }
}
