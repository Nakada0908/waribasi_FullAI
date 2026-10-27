using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace Waribasi.Networking
{
    public static class OnlineServicesBootstrap
    {
        private static Task initialization;

        public static Task EnsureReadyAsync()
        {
            if (initialization == null || initialization.IsFaulted || initialization.IsCanceled ||
                (initialization.IsCompleted && !AuthenticationService.Instance.IsSignedIn))
            {
                initialization = InitializeAsync();
            }
            return initialization;
        }

        private static async Task InitializeAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                options.SetProfile("Player_" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
    }
}
