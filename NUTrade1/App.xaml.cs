using Microsoft.Extensions.DependencyInjection;
using NUTrade1.Services;

namespace NUTrade1
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            InitializeComponent();
            _services = services;

            // NUTrade is designed for a white page. Without this the app follows the
            // phone's dark mode and swaps to the dark palette (near-black background).
            UserAppTheme = AppTheme.Light;

            // Sweep any photo copies an interrupted post left in the cache.
            _ = ImageCache.TrimAsync();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
#if DEBUG
            if (SkeletonPreview.TryCreate(_services) is { } preview) return new Window(preview); // TEMPORARY
#endif
            var shell = _services.GetRequiredService<AppShell>();
            return new Window(shell);
        }
    }
}
