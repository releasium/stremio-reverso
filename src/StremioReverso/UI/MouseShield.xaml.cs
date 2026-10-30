using System.Windows;
using StremioReverso.Core;

namespace StremioReverso.UI;

/// Invisible window over Stremio while paused. Stremio stops seeing mouse movement, so its
/// controls hide and stay hidden, and the subtitle stops jumping up above them.
public partial class MouseShield : Window
{
    public MouseShield()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
    }

    /// Call before showing the overlay and popup, so they end up above the shield.
    public void Cover(Box stremio) => WindowPlacement.Place(this, stremio);
}
