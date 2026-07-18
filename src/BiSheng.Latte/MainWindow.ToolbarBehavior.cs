using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using BiSheng.Latte.Models;

namespace BiSheng.Latte;

/// <summary>工具栏固定显示 / 悬停淡入淡出行为</summary>
public partial class MainWindow
{
    private const double FadeInSeconds = 0.2;
    private const double FadeOutSeconds = 0.3;
    private const double FadeOutDelaySeconds = 1.0;

    private readonly List<ToolbarFadeHost> _toolbarFadeHosts = [];

    internal void ApplyToolbarVisibilityBehavior(ToolbarVisibilityMode mode)
    {
        EnsureToolbarFadeHosts();
        foreach (var host in _toolbarFadeHosts)
        {
            host.Apply(mode);
        }
    }

    private void EnsureToolbarFadeHosts()
    {
        if (_toolbarFadeHosts.Count > 0)
        {
            return;
        }

        _toolbarFadeHosts.Add(new ToolbarFadeHost(ToolbarBorder));
        _toolbarFadeHosts.Add(new ToolbarFadeHost(SidebarToolbarBorder));
    }

    private sealed class ToolbarFadeHost
    {
        private readonly Border _border;
        private bool _autoHideEnabled;
        private Storyboard? _activeStoryboard;

        public ToolbarFadeHost(Border border) => _border = border;

        public void Apply(ToolbarVisibilityMode mode)
        {
            StopAnimation();
            DetachHandlers();

            if (mode == ToolbarVisibilityMode.Fixed)
            {
                _autoHideEnabled = false;
                _border.Opacity = 1;
                return;
            }

            _autoHideEnabled = true;
            _border.Opacity = _border.IsMouseOver ? 1 : 0;
            _border.MouseEnter += OnMouseEnter;
            _border.MouseLeave += OnMouseLeave;
        }

        private void DetachHandlers()
        {
            _border.MouseEnter -= OnMouseEnter;
            _border.MouseLeave -= OnMouseLeave;
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (!_autoHideEnabled)
            {
                return;
            }

            FadeTo(1, FadeInSeconds, 0);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_autoHideEnabled)
            {
                return;
            }

            FadeTo(0, FadeOutSeconds, FadeOutDelaySeconds);
        }

        private void FadeTo(double to, double durationSeconds, double beginDelaySeconds)
        {
            StopAnimation();

            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds)
            };

            _activeStoryboard = new Storyboard();
            Storyboard.SetTarget(animation, _border);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Border.OpacityProperty));
            _activeStoryboard.Children.Add(animation);
            _activeStoryboard.Begin();
        }

        private void StopAnimation()
        {
            _activeStoryboard?.Stop();
            _activeStoryboard = null;
        }
    }
}
