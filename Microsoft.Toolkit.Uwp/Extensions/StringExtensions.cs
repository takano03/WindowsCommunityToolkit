// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Resources;
using Windows.UI;
using Windows.UI.WindowManagement;

namespace Microsoft.Toolkit.Uwp.Extensions
{
    /// <summary>
    /// UWP specific helpers for working with strings.
    /// </summary>
    public static class StringExtensions
    {
        private static readonly ResourceLoader IndependentLoader = ResourceLoader.GetForViewIndependentUse();

        /// <summary>
        /// Retrieves the provided resource for the current view context.
        /// </summary>
        /// <param name="resourceKey">Resource key to retrieve.</param>
        /// <param name="uiContext"><see cref="UIContext"/> to be used to get the <paramref name="resourceKey"/> from.
        /// You can retrieve this from a <see cref="UIElement.UIContext"/>, <see cref="XamlRoot.UIContext"/> (XamlIslands), or <see cref="Window.UIContext"/>.</param>
        /// <returns>string value for given resource or empty string if not found.</returns>
        public static string GetViewLocalized(this string resourceKey, UIContext uiContext = null)
        {
            if (uiContext != null)
            {
                var resourceLoader = ResourceLoader.GetForUIContext(uiContext);
                return resourceLoader.GetString(resourceKey);
            }
            else
            {
                return ResourceLoader.GetForCurrentView().GetString(resourceKey);
            }
        }

        /// <summary>
        /// Retrieves the provided resource for the given key for use independent of the UI thread.
        /// </summary>
        /// <param name="resourceKey">Resource key to retrieve.</param>
        /// <param name="uiContext"><see cref="UIContext"/> to be used to get the <paramref name="resourceKey"/> from.
        /// You can retrieve this from a <see cref="UIElement.UIContext"/>, <see cref="XamlRoot.UIContext"/> (XamlIslands), or <see cref="Window.UIContext"/>.</param>
        /// <returns>string value for given resource or empty string if not found.</returns>
        public static string GetLocalized(this string resourceKey, UIContext uiContext = null)
        {
            if (uiContext != null)
            {
                var resourceLoader = ResourceLoader.GetForUIContext(uiContext);
                return resourceLoader.GetString(resourceKey);
            }
            else
            {
                return IndependentLoader.GetString(resourceKey);
            }
        }

        /// <summary>
        /// Retrieves the provided resource for the given key for use independent of the UI thread.
        /// </summary>
        /// <param name="resourceKey">Resource key to retrieve.</param>
        /// <param name="resourcePath">Resource path to retrieve.</param>
        /// <returns>string value for given resource or empty string if not found.</returns>
        public static string GetLocalized(this string resourceKey, string resourcePath)
            => ResourceLoader.GetForViewIndependentUse(resourcePath).GetString(resourceKey);
    }
}
