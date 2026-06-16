using System;
using Sandbox;

namespace Minimal.Clan;

internal sealed class ClanManager : Component
{
    public static ClanManager Instance { get; private set; }

    protected override void OnAwake()
    {
        if (Instance == null)
            Instance = this;
    }

    protected override void OnDestroy()
    {
        if (Instance != null)
            Instance = null;
    }
}
