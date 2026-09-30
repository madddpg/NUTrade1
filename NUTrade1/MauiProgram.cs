using Microsoft.Extensions.Logging;
using NUTrade1.Core;
using NUTrade1.Services;
using NUTrade1.Services.Firebase;
using NUTrade1.ViewModels;
using NUTrade1.Views;

namespace NUTrade1
{
    public static class MauiProgram
    {
        /// <summary>
        /// Flips the whole app back onto the in-memory <c>Stub*</c> services. They hold no
        /// seeded content any more, so this is a way to click through the UI with no
        /// network and no Firebase project — not a demo mode. Ship it as <c>false</c>.
        /// </summary>
        private static readonly bool UseStubServices = false;

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    // Inter throughout, as in the 2026-09 mockups; Lobster for the
                    // hand-lettered "Make it yours." on the home hero.
                    fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                    fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                    fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                    fonts.AddFont("Inter-Bold.ttf", "InterBold");
                    fonts.AddFont("Inter-ExtraBold.ttf", "InterExtraBold");
                    fonts.AddFont("Lobster-Regular.ttf", "Lobster");
                });

            StripPlatformInputChrome();
            RegisterServices(builder.Services);
            RegisterViewModels(builder.Services);
            RegisterPages(builder.Services);

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        /// <summary>
        /// Every text field and dropdown sits inside the rounded InputShell border from the
        /// mockups, so each platform's own chrome (Android's underline, the WinUI box and
        /// focus bar, the iOS rounded rect) is removed to leave just that one outline.
        /// </summary>
        private static void StripPlatformInputChrome()
        {
            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NUTradeBorderless", (handler, _) =>
            {
#if ANDROID
                handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
                StripTextBox(handler.PlatformView);
#elif IOS || MACCATALYST
                handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
            });

            Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NUTradeBorderless", (handler, _) =>
            {
#if ANDROID
                handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
                StripTextBox(handler.PlatformView);
#endif
            });

            Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("NUTradeBorderless", (handler, _) =>
            {
#if ANDROID
                handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
                handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
#endif
            });
        }

#if WINDOWS
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.UI.Xaml.Controls.TextBox, object> StrippedBoxes = new();

        private static void StripTextBox(Microsoft.UI.Xaml.Controls.TextBox box)
        {
            box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);

            // MAUI re-runs this mapping when the Entry changes, but WinUI locks a control's
            // resources once it has loaded — so this happens once per box, and a failure is
            // cosmetic, never fatal.
            if (StrippedBoxes.TryGetValue(box, out _)) return;
            StrippedBoxes.Add(box, new object());
            try
            {
                // WinUI swaps these in on hover and focus (a transparent focused border also
                // hides the accent bar). Each key gets its own brush: a XAML object can't sit
                // in a dictionary twice.
                foreach (var key in new[]
                {
                    "TextControlBorderBrush", "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver",
                    "TextControlBackgroundFocused", "TextControlBackgroundPointerOver",
                })
                {
                    box.Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Already loaded and locked; it keeps the default hover/focus look.
            }
        }
#endif

        private static void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<AppShell>();
            services.AddSingleton<INavigationService, ShellNavigationService>();
            services.AddSingleton<WinWatcher>();
            services.AddSingleton<IQrImageSaver, QrImageSaver>();

            if (UseStubServices) RegisterStubBackend(services);
            else RegisterFirebaseBackend(services);
        }

        /// <summary>
        /// The real backend: Firebase Auth, Firestore and Storage over REST, plus the
        /// callable Cloud Functions that own every privileged write. One HttpClient is
        /// shared by all of them — creating one per service is the classic way to
        /// exhaust sockets.
        /// </summary>
        private static void RegisterFirebaseBackend(IServiceCollection services)
        {
            services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

            // One instance, two roles: IAuthService for the app, IFirebaseTokenProvider
            // for the transports that need to attach its ID token.
            services.AddSingleton<IGoogleAuthBroker, GoogleAuthBroker>();
            services.AddSingleton<FirebaseAuthService>();
            services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<FirebaseAuthService>());
            services.AddSingleton<IFirebaseTokenProvider>(sp => sp.GetRequiredService<FirebaseAuthService>());

            services.AddSingleton<FirestoreClient>();
            services.AddSingleton<FunctionsClient>();

            services.AddSingleton<IStorageService, FirebaseStorageService>();
            services.AddSingleton<IUserService, FirestoreUserService>();
            services.AddSingleton<IListingService, FirestoreListingService>();
            services.AddSingleton<IBidService, FirestoreBidService>();
            services.AddSingleton<IChatService, FirestoreChatService>();
            services.AddSingleton<IPaymentService, FunctionsPaymentService>();
            services.AddSingleton<IOrderService, FirestoreOrderService>();
            services.AddSingleton<IListingModerationService, FunctionsListingModerationService>();
            services.AddSingleton<IEmailVerificationService, EmailVerificationService>();
            services.AddSingleton<IRegistrationService, FunctionsRegistrationService>();
            services.AddSingleton<IPasswordResetService, FunctionsPasswordResetService>();
            services.AddSingleton<IDepositService, FunctionsDepositService>();
            services.AddSingleton<IWalletService, FirestoreWalletService>();
            services.AddSingleton<IPayoutModerationService, FunctionsPayoutModerationService>();
        }

        private static void RegisterStubBackend(IServiceCollection services)
        {
            services.AddSingleton<IAuthService, StubAuthService>();
            services.AddSingleton<IUserService, StubUserService>();
            services.AddSingleton<IListingService, StubListingService>();
            services.AddSingleton<IBidService, StubBidService>();
            services.AddSingleton<IChatService, StubChatService>();
            services.AddSingleton<IPaymentService, StubPaymentService>();
            services.AddSingleton<IStorageService, StubStorageService>();
            services.AddSingleton<IOrderService, StubOrderService>();
            services.AddSingleton<IListingModerationService, StubListingModerationService>();
            services.AddSingleton<IEmailVerificationService, StubEmailVerificationService>();
            services.AddSingleton<IRegistrationService, StubRegistrationService>();
            services.AddSingleton<IPasswordResetService, StubPasswordResetService>();

            // One instance serves both roles offline so an admin decision and the student
            // balance it moves are the same in-memory state.
            services.AddSingleton<IDepositService, StubDepositService>();
            services.AddSingleton<StubWalletService>();
            services.AddSingleton<IWalletService>(sp => sp.GetRequiredService<StubWalletService>());
            services.AddSingleton<IPayoutModerationService>(sp => sp.GetRequiredService<StubWalletService>());
        }

        private static void RegisterViewModels(IServiceCollection services)
        {
            services.AddTransient<WelcomeViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<RegisterViewModel>();
            services.AddTransient<ForgotPasswordViewModel>();
            services.AddTransient<VerifyEmailViewModel>();
            services.AddTransient<ProfileSetupViewModel>();
            services.AddTransient<FeedViewModel>();
            services.AddTransient<ListingDetailViewModel>();
            services.AddTransient<PostTradeViewModel>();
            services.AddTransient<PaymentViewModel>();
            services.AddTransient<OrderPaymentViewModel>();
            services.AddTransient<OffersViewModel>();
            services.AddTransient<ChatListViewModel>();
            services.AddTransient<ChatRoomViewModel>();
            services.AddTransient<ProfileViewModel>();
            services.AddTransient<ListingApprovalsViewModel>();
            services.AddTransient<WalletViewModel>();
            services.AddTransient<BidDepositViewModel>();
            services.AddTransient<PayoutsViewModel>();
            services.AddTransient<PhotoViewerViewModel>();
            services.AddTransient<ReviewListingViewModel>();
        }

        private static void RegisterPages(IServiceCollection services)
        {
            services.AddTransient<WelcomePage>();
            services.AddTransient<LoginPage>();
            services.AddTransient<RegisterPage>();
            services.AddTransient<ForgotPasswordPage>();
            services.AddTransient<VerifyEmailPage>();
            services.AddTransient<ProfileSetupPage>();
            services.AddTransient<FeedPage>();
            services.AddTransient<ListingDetailPage>();
            services.AddTransient<PostTradePage>();
            services.AddTransient<PaymentPage>();
            services.AddTransient<OrderPaymentPage>();
            services.AddTransient<OffersPage>();
            services.AddTransient<ChatListPage>();
            services.AddTransient<ChatRoomPage>();
            services.AddTransient<ProfilePage>();
            services.AddTransient<ListingApprovalsPage>();
            services.AddTransient<WalletPage>();
            services.AddTransient<BidDepositPage>();
            services.AddTransient<PayoutsPage>();
            services.AddTransient<PhotoViewerPage>();
            services.AddTransient<ReviewListingPage>();
        }
    }
}
