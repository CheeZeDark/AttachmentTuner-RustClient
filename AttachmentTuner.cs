using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AttachmentTuner", "RikkoMatsumato", "1.3.4")]
    [Description("Tune attachment-influenced hipfire spread and sway on weapons. Automatically removes sway/twitch for default attachments.")]
    public class AttachmentTuner : RustPlugin
    {
        #region Config

        private PluginConfig _config;
        private const string permUse = "attachmenttuner.use";

        public class PluginConfig
        {
            // Per-attachment multipliers (hipcone, sway, swayspeed)
            // Setting all to 0 disables sway/twitch automatically
            public Dictionary<string, AttachmentMultipliers> Attachments { get; set; } = new Dictionary<string, AttachmentMultipliers>()
            {
                ["weapon.mod.holosight"] = new AttachmentMultipliers { HipAimConeMult = 0f, AimSwayMult = 0f, AimSwaySpeedMult = 0f },
                ["weapon.mod.lasersight"] = new AttachmentMultipliers { HipAimConeMult = 0f, AimSwayMult = 0f, AimSwaySpeedMult = 0f },
                ["weapon.mod.silencer"] = new AttachmentMultipliers { HipAimConeMult = 0f, AimSwayMult = 0f, AimSwaySpeedMult = 0f },
                ["weapon.mod.muzzlebrake"] = new AttachmentMultipliers { HipAimConeMult = 0f, AimSwayMult = 0f, AimSwaySpeedMult = 0f },
                ["weapon.mod.flashlight"] = new AttachmentMultipliers { HipAimConeMult = 0f, AimSwayMult = 0f, AimSwaySpeedMult = 0f },
                ["weapon.mod.burstmodule"] = new AttachmentMultipliers { HipAimConeMult = 0f, AimSwayMult = 0f, AimSwaySpeedMult = 0f }
            };

            // Clamps
            public float MinHipAimCone = 0.0f;
            public float MaxHipAimCone = 0.0f;   // Max also 0
            public float MinAimSway = 0.0f;
            public float MaxAimSway = 0.0f;
            public float MinAimSwaySpeed = 0.0f;
            public float MaxAimSwaySpeed = 0.0f;
        }

        public class AttachmentMultipliers
        {
            public float HipAimConeMult = 0f;
            public float AimSwayMult = 0f;
            public float AimSwaySpeedMult = 0f;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception("Config read null");
            }
            catch (Exception e)
            {
                PrintError($"Config error: {e.Message}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region State & Helpers

        private readonly Dictionary<ItemId, OriginalProjectileStats> _originalStats = new Dictionary<ItemId, OriginalProjectileStats>();

        private class OriginalProjectileStats
        {
            public float HipAimCone;
            public float AimSway;
            public float AimSwaySpeed;
        }

        private bool TryGetBaseProjectile(Item item, out BaseProjectile bp)
        {
            bp = null;
            if (item == null) return false;
            var ent = item.GetHeldEntity() as BaseProjectile;
            if (ent == null || ent.IsDestroyed) return false;
            bp = ent;
            return true;
        }

        private void SnapshotIfMissing(Item item, BaseProjectile bp)
        {
            if (bp == null || item == null) return;
            if (_originalStats.ContainsKey(item.uid)) return;

            _originalStats[item.uid] = new OriginalProjectileStats
            {
                HipAimCone = bp.hipAimCone,
                AimSway = bp.aimSway,
                AimSwaySpeed = bp.aimSwaySpeed
            };
        }

        private void ApplyAttachmentTuning(Item weaponItem)
        {
            if (!TryGetBaseProjectile(weaponItem, out var bp)) return;

            SnapshotIfMissing(weaponItem, bp);
            var orig = _originalStats[weaponItem.uid];

            float hipAimCone = orig.HipAimCone;
            float aimSway = orig.AimSway;
            float aimSwaySpeed = orig.AimSwaySpeed;

            var mods = weaponItem.contents;
            if (mods != null)
            {
                foreach (var modItem in mods.itemList)
                {
                    if (modItem?.info == null) continue;
                    var shortname = modItem.info.shortname;
                    if (string.IsNullOrEmpty(shortname)) continue;

                    if (_config.Attachments.TryGetValue(shortname, out var mult))
                    {
                        hipAimCone *= mult.HipAimConeMult;
                        aimSway *= mult.AimSwayMult;
                        aimSwaySpeed *= mult.AimSwaySpeedMult;
                    }
                }
            }

            // Clamp to 0
            hipAimCone = Mathf.Clamp(hipAimCone, _config.MinHipAimCone, _config.MaxHipAimCone);
            aimSway = Mathf.Clamp(aimSway, _config.MinAimSway, _config.MaxAimSway);
            aimSwaySpeed = Mathf.Clamp(aimSwaySpeed, _config.MinAimSwaySpeed, _config.MaxAimSwaySpeed);

            bp.hipAimCone = hipAimCone;
            bp.aimSway = aimSway;
            bp.aimSwaySpeed = aimSwaySpeed;
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(permUse, this);
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (newItem == null) return;
            timer.Once(0f, () =>
            {
                if (newItem == null) return;
                if (TryGetBaseProjectile(newItem, out _))
                    ApplyAttachmentTuning(newItem);
            });
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null) return;
            var parentItem = container.parent;
            if (parentItem == null) return;

            if (TryGetBaseProjectile(parentItem, out _))
                timer.Once(0f, () => ApplyAttachmentTuning(parentItem));
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null) return;
            var parentItem = container.parent;
            if (parentItem == null) return;

            if (TryGetBaseProjectile(parentItem, out _))
                timer.Once(0f, () => ApplyAttachmentTuning(parentItem));
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item == null) return;
            _originalStats.Remove(item.uid);
        }

        private void OnEntityKill(BaseNetworkable net)
        {
            var held = net as HeldEntity;
            if (held == null) return;
            var item = held.GetItem();
            if (item != null)
                _originalStats.Remove(item.uid);
        }

        #endregion

        #region Commands

        [ChatCommand("tuneattach")]
        private void CmdTuneAttach(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                player.ChatMessage($"[{Name}] You don’t have permission to use this command.");
                return;
            }

            if (args.Length < 3)
            {
                player.ChatMessage("Usage: /tuneattach <attachment_shortname> <property> <value>\nProperties: hipcone | sway | swayspeed");
                return;
            }

            var attach = args[0].ToLower();
            var prop = args[1].ToLower();

            if (!float.TryParse(args[2], out var val))
            {
                player.ChatMessage("Invalid value (must be a number).");
                return;
            }

            if (!_config.Attachments.TryGetValue(attach, out var mult))
            {
                mult = new AttachmentMultipliers();
                _config.Attachments[attach] = mult;
            }

            switch (prop)
            {
                case "hipcone": mult.HipAimConeMult = val; break;
                case "sway": mult.AimSwayMult = val; break;
                case "swayspeed": mult.AimSwaySpeedMult = val; break;
                default:
                    player.ChatMessage("Unknown property. Use: hipcone | sway | swayspeed");
                    return;
            }

            SaveConfig();
            player.ChatMessage($"[{Name}] Set {attach} {prop} multiplier = {val}");

            var item = player.GetActiveItem();
            if (item != null && TryGetBaseProjectile(item, out _))
                ApplyAttachmentTuning(item);
        }

        [ChatCommand("showattach")]
        private void CmdShowAttach(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                player.ChatMessage($"[{Name}] You don’t have permission to use this command.");
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage("Usage: /showattach <attachment_shortname>");
                return;
            }

            var attach = args[0].ToLower();
            if (!_config.Attachments.TryGetValue(attach, out var mult))
            {
                player.ChatMessage($"No entry for '{attach}'.");
                return;
            }

            player.ChatMessage(
                $"[{Name}] {attach}\n" +
                $"- hipcone x{mult.HipAimConeMult}\n" +
                $"- sway x{mult.AimSwayMult}\n" +
                $"- swayspeed x{mult.AimSwaySpeedMult}"
            );
        }

        #endregion
    }
}
