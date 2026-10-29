using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Kudubrowse.Core.Passwords;

namespace Kudubrowse.Views
{
    /// <summary>The window's password parts: the "Save password?" bar and the list of saved logins next to a login box.</summary>
    public sealed partial class MainWindow
    {
        private Border _saveBar;
        private TextBlock _saveText;
        private Button _saveButton, _neverButton;
        private Popup _fillPopup;
        private StackPanel _fillItems;

        private UIElement InitPasswords()
        {
            _saveText = Ui.Text(string.Empty, 13, wrap: true);
            _saveButton = Ui.Small(Ui.Button("Save", null, "PrimaryButton"));
            _neverButton = Ui.Small(Ui.Button("Never for this site"));
            var close = Ui.Button(null, "IconClose", "GhostButton");
            close.Width = close.MinWidth = 30;
            close.Padding = new Thickness(0);
            close.ToolTip = "Not now";
            _saveButton.Click += (s, e) => SavePendingLogin();
            _neverButton.Click += (s, e) =>
            {
                var p = Current?.PendingLogin;
                if (p != null) PasswordStore.Vault.NeverFor(p.Value.Url);
                DismissPendingLogin();
            };
            close.Click += (s, e) => DismissPendingLogin();
            var row = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto, Ui.Auto);
            row.Children.Add(Ui.Icon("IconLock", 17).Margin(0, 0, 10, 0).At(0, 0));
            row.Children.Add(_saveText.At(0, 1));
            row.Children.Add(_neverButton.Margin(10, 0, 0, 0).At(0, 2));
            row.Children.Add(_saveButton.Margin(8, 0, 0, 0).At(0, 3));
            row.Children.Add(close.Margin(6, 0, 0, 0).At(0, 4));
            _saveBar = new Border { Child = row, Padding = new Thickness(14, 8, 10, 8), BorderThickness = new Thickness(0, 0, 0, 1), Visibility = Visibility.Collapsed }
                .Res(Border.BackgroundProperty, "BannerBg").Res(Border.BorderBrushProperty, "BorderBrush");

            _fillItems = new StackPanel { MinWidth = 260 };
            _fillPopup = new Popup
            {
                Child = new Border
                {
                    Child = _fillItems,
                    Padding = new Thickness(4),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.25 }
                }.Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush"),
                Placement = PlacementMode.Relative,
                StaysOpen = false,
                AllowsTransparency = true
            };
            return _saveBar;
        }

        private void WatchPasswords(BrowserTab tab)
        {
            tab.LoginSent += OnLoginSent;
            tab.LoginFieldFocused += OnLoginField;
            tab.LoginFieldLeft += t =>
            {
                if (t == Current && !_fillPopup.IsMouseOver) _fillPopup.IsOpen = false;
            };
            // The list belongs to the page it was shown for: it goes away as soon as the page moves on.
            tab.Navigating += t =>
            {
                if (t == _fillTab) _fillPopup.IsOpen = false;
            };
        }

        // ================================================================ saving

        private void OnLoginSent(BrowserTab tab, string url, string username, string password)
        {
            if (!Settings.OfferToSavePasswords) return;
            var kind = PasswordStore.Vault.Check(url, username, password);
            if (kind == Vault.SaveKind.Nothing) return;
            tab.PendingLogin = (url, username, password, kind);
            if (tab == Current) UpdateSaveBar();
        }

        public bool SaveBarShown => _saveBar.Visibility == Visibility.Visible;

        private void UpdateSaveBar()
        {
            var p = Current?.PendingLogin;
            if (p == null)
            {
                _saveBar.Visibility = Visibility.Collapsed;
                return;
            }
            var site = Vault.HostOf(Vault.OriginOf(p.Value.Url));
            var who = string.IsNullOrEmpty(p.Value.Username) ? string.Empty : " for " + p.Value.Username;
            var update = p.Value.Kind == Vault.SaveKind.Update;
            _saveText.Text = update ? $"Update the saved password{who} on {site}?" : $"Save the password{who} on {site}?";
            _saveButton.Content = update ? "Update" : "Save";
            _neverButton.Visibility = update ? Visibility.Collapsed : Visibility.Visible;
            _saveBar.Visibility = Visibility.Visible;
        }

        public void SavePendingLogin()
        {
            var p = Current?.PendingLogin;
            if (p == null) return;
            PasswordStore.Vault.Save(p.Value.Url, p.Value.Username, p.Value.Password);
            DismissPendingLogin();
        }

        private void DismissPendingLogin()
        {
            if (Current != null) Current.PendingLogin = null;
            UpdateSaveBar();
        }

        // ================================================================ filling

        /// <summary>What the list next to the login box offers (for the self-test).</summary>
        private BrowserTab _fillTab;

        /// <summary>The list of saved logins is showing (for the self-test).</summary>
        public bool FillListOpen => _fillPopup.IsOpen;

        public IReadOnlyList<string> FillChoices => _fillItems.Children.OfType<Button>().Select(b => b.Tag as string).ToList();

        private void OnLoginField(BrowserTab tab, Point at, bool isNew)
        {
            if (tab != Current || tab.View == null) return;
            var origin = Vault.OriginOf(tab.View.CoreWebView2?.Source);
            if (origin == null) return;
            _fillTab = tab;
            var logins = PasswordStore.Vault.For(origin);
            _fillItems.Children.Clear();
            Button Choice(string title, string sub, string icon, string tag, Action act)
            {
                var b = Ui.Button(null, icon, "GhostButton");
                var text = Ui.Column(Ui.Text(title, 13), Ui.Subtle(sub, 11.5, false));
                b.Content = Ui.Row(Ui.Icon(icon, 16), text.Margin(10, 0, 0, 0));
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Padding = new Thickness(10, 6, 10, 6);
                b.Tag = tag;
                System.Windows.Automation.AutomationProperties.SetName(b, title);
                b.Click += (s, e) =>
                {
                    _fillPopup.IsOpen = false;
                    act();
                };
                return b;
            }
            foreach (var l in logins.Take(6))
            {
                var login = l;
                _fillItems.Children.Add(Choice(string.IsNullOrEmpty(l.Username) ? "(no name)" : l.Username, "••••••••  ·  " + l.Site, "IconUser", "login:" + l.Username, () =>
                {
                    if (tab.Fill(origin, login.Username, login.Password)) PasswordStore.Vault.MarkUsed(login);
                }));
            }
            if (isNew)
            {
                var generated = PasswordTools.Generate();
                _fillItems.Children.Add(Choice("Use a strong password", generated, "IconLock", "generated", () => tab.FillGenerated(origin, generated)));
            }
            if (_fillItems.Children.Count == 0)
            {
                _fillPopup.IsOpen = false;
                return;
            }
            var manage = Ui.Link("Manage passwords", (s, e) =>
            {
                _fillPopup.IsOpen = false;
                PasswordsWindow.Show(this);
            });
            manage.Margin = new Thickness(10, 6, 0, 4);
            manage.HorizontalAlignment = HorizontalAlignment.Left;
            _fillItems.Children.Add(manage);
            // Page pixels → window units (the page may be zoomed).
            var zoom = tab.View.ZoomFactor;
            _fillPopup.PlacementTarget = tab.View;
            _fillPopup.HorizontalOffset = at.X * zoom;
            _fillPopup.VerticalOffset = at.Y * zoom + 2;
            _fillPopup.IsOpen = true;
        }

        /// <summary>For the self-test: picks a choice in the list, as a click would.</summary>
        public void ChooseFill(string tag)
        {
            var b = _fillItems.Children.OfType<Button>().FirstOrDefault(x => x.Tag as string == tag);
            b?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }
    }
}
