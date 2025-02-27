﻿using CoreSystems.Platform;
using CoreSystems.Support;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;

namespace CoreSystems
{
    public partial class Session
    {
        private bool ServerClientMouseEvent(PacketObj data)
        {
            var packet = data.Packet;
            var inputPacket = (InputPacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);

            if (ent == null) return Error(data, Msg("Entity"));
            if (inputPacket.Data == null) return Error(data, Msg("BaseData"));

            long playerId;
            if (SteamToPlayer.TryGetValue(packet.SenderId, out playerId))
            {
                if (PlayerMouseStates.ContainsKey(playerId))
                    PlayerMouseStates[playerId].Sync(inputPacket.Data);
                else
                    PlayerMouseStates[playerId] = new InputStateData(inputPacket.Data);

                PacketsToClient.Add(new PacketInfo { Entity = ent, Packet = inputPacket });

                data.Report.PacketValid = true;
            }
            else
                return Error(data, Msg("Player Not Found"));

            return true;
        }

        private bool ServerActiveControlUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var dPacket = (BoolUpdatePacket)packet;
            var entity = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var topEntity = entity?.GetTopMostParent();
            if (topEntity == null) return Error(data, Msg("TopEntity"));

            Ai ai;
            long playerId = 0;
            if (EntityToMasterAi.TryGetValue(topEntity, out ai) && SteamToPlayer.TryGetValue(packet.SenderId, out playerId))
            {
                ai.Construct.UpdateConstructsPlayers(entity, playerId, dPacket.Data);
                data.Report.PacketValid = true;
            }
            else Log.Line($"ServerActiveControlUpdate: ai:{ai != null} - targetingAi:{EntityAIs.ContainsKey(topEntity)} - masterAi:{EntityToMasterAi.ContainsKey(topEntity)} - IdToComp:{IdToCompMap.ContainsKey(entity.EntityId)} - playerId:{playerId}({packet.SenderId}) - marked:{entity.MarkedForClose}({topEntity.MarkedForClose}) - active:{dPacket.Data}");

            return true;
        }

        private bool ServerUpdateSetting(PacketObj data)
        {
            var packet = data.Packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>();
            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg($"CompId: {packet.EntityId}", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            switch (packet.PType)
            {
                case PacketType.RequestSetRof:
                    {
                        BlockUi.RequestSetRof(comp.CoreEntity as IMyTerminalBlock, ((FloatUpdatePacket)packet).Data);
                        break;
                    }
                case PacketType.RequestSetRange:
                    {
                        BlockUi.RequestSetRange(comp.CoreEntity as IMyTerminalBlock, ((FloatUpdatePacket)packet).Data);
                        break;
                    }
                case PacketType.RequestSetDps:
                    {
                        BlockUi.RequestSetDps(comp.CoreEntity as IMyTerminalBlock, ((FloatUpdatePacket)packet).Data);
                        break;
                    }
                case PacketType.RequestSetGuidance:
                    {
                        BlockUi.RequestSetGuidance(comp.CoreEntity as IMyTerminalBlock, ((BoolUpdatePacket)packet).Data);
                        break;
                    }
                case PacketType.RequestSetOverload:
                    {
                        BlockUi.RequestSetOverload(comp.CoreEntity as IMyTerminalBlock, ((BoolUpdatePacket)packet).Data);
                        break;
                    }
            }

            data.Report.PacketValid = true;


            return true;
        }
        private bool ServerAimTargetUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var targetPacket = (FakeTargetPacket)packet;
            var myGrid = MyEntities.GetEntityByIdOrDefault(packet.EntityId) as MyCubeGrid;

            if (myGrid == null) return Error(data, Msg($"GridId:{packet.EntityId} - entityExists:{MyEntities.EntityExists(packet.EntityId)}"));


            Ai ai;
            long playerId;
            if (EntityAIs.TryGetValue(myGrid, out ai) && SteamToPlayer.TryGetValue(packet.SenderId, out playerId))
            {
                Ai.FakeTargets fakeTargets;
                if (PlayerDummyTargets.TryGetValue(playerId, out fakeTargets))
                {
                    fakeTargets.ManualTarget.Sync(targetPacket, ai);
                    PacketsToClient.Add(new PacketInfo { Entity = myGrid, Packet = targetPacket });

                    data.Report.PacketValid = true;
                }
            }
            else
                return Error(data, Msg($"GridAi not found, is marked:{myGrid.MarkedForClose}, has root:{EntityToMasterAi.ContainsKey(myGrid)}"));

            return true;
        }

        private bool ServerMarkedTargetUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var targetPacket = (FakeTargetPacket)packet;
            var myGrid = MyEntities.GetEntityByIdOrDefault(packet.EntityId) as MyCubeGrid;

            if (myGrid == null) return Error(data, Msg($"GridId:{packet.EntityId} - entityExists:{MyEntities.EntityExists(packet.EntityId)}"));


            Ai.FakeTargets fakeTargets;
            Ai ai;
            long playerId;
            if (EntityAIs.TryGetValue(myGrid, out ai) && SteamToPlayer.TryGetValue(packet.SenderId, out playerId) && PlayerDummyTargets.TryGetValue(playerId, out fakeTargets))
            {
                fakeTargets.PaintedTarget.Sync(targetPacket, ai);
                PacketsToClient.Add(new PacketInfo { Entity = myGrid, Packet = targetPacket });

                data.Report.PacketValid = true;
            }
            else
                return Error(data, Msg($"GridAi not found, is marked:{myGrid.MarkedForClose}, has root:{EntityToMasterAi.ContainsKey(myGrid)}"));

            return true;
        }

        private bool ServerAmmoCycleRequest(PacketObj data)
        {
            var packet = data.Packet;
            var cyclePacket = (AmmoCycleRequestPacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            comp.Data.Repo.Values.State.PlayerId = cyclePacket.PlayerId;
            comp.Platform.Weapons[cyclePacket.PartId].ChangeAmmo(cyclePacket.NewAmmoId);
            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerPlayerControlRequest(PacketObj data)
        {
            var packet = data.Packet;
            var controlPacket = (PlayerControlRequestPacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            comp.Data.Repo.Values.State.PlayerId = controlPacket.PlayerId;
            comp.Data.Repo.Values.State.Control = controlPacket.Mode;
            SendComp(comp);
            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerReticleUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var reticlePacket = (BoolUpdatePacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            comp.Data.Repo.Values.State.TrackingReticle = reticlePacket.Data;
            SendState(comp);

            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerCountingDownUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var countingDownPacket = (BoolUpdatePacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            comp.Data.Repo.Values.State.CountingDown = countingDownPacket.Data;
            SendState(comp);

            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerCriticalReactionUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var countingDownPacket = (BoolUpdatePacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            comp.Data.Repo.Values.State.CountingDown = countingDownPacket.Data;
            SendState(comp);

            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerOverRidesUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var overRidesPacket = (OverRidesPacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId, null, true);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;
            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            Weapon.WeaponComponent.RequestSetValue(comp, overRidesPacket.Setting, overRidesPacket.Value, SteamToPlayer[overRidesPacket.SenderId]);
            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerClientAiExists(PacketObj data)
        {
            var packet = data.Packet;

            if (PlayerEntityIdInRange.ContainsKey(packet.SenderId))
            {
                switch (packet.PType)
                {
                    case PacketType.ClientAiRemove:
                        PlayerEntityIdInRange[packet.SenderId].Remove(packet.EntityId);
                        break;
                    case PacketType.ClientAiAdd:
                        PlayerEntityIdInRange[packet.SenderId].Add(packet.EntityId);
                        break;
                }
            }
            else return Error(data, Msg("SenderId not found"));

            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerRequestShootUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var shootStatePacket = (ShootStatePacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>() as Weapon.WeaponComponent;

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            comp.RequestShootUpdate(shootStatePacket.Action, shootStatePacket.PlayerId);
            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerFocusUpdate(PacketObj data)
        {
            var packet = data.Packet;
            var focusPacket = (FocusPacket)packet;
            var myGrid = MyEntities.GetEntityByIdOrDefault(packet.EntityId) as MyCubeGrid;

            if (myGrid == null) return Error(data, Msg("Grid"));

            Ai ai;
            if (EntityToMasterAi.TryGetValue(myGrid, out ai))
            {
                var targetGrid = MyEntities.GetEntityByIdOrDefault(focusPacket.TargetId) as MyCubeGrid;

                switch (packet.PType)
                {
                    case PacketType.FocusUpdate:
                        if (targetGrid != null)
                            ai.Construct.Focus.ServerAddFocus(targetGrid, ai);
                        break;
                    case PacketType.NextActiveUpdate:
                        ai.Construct.Focus.ServerNextActive(focusPacket.AddSecondary, ai);
                        break;
                    case PacketType.ReleaseActiveUpdate:
                        ai.Construct.Focus.RequestReleaseActive(ai);
                        break;
                    case PacketType.FocusLockUpdate:
                        ai.Construct.Focus.ServerCycleLock(ai);
                        break;
                }

                data.Report.PacketValid = true;
            }
            else
                return Error(data, Msg($"GridAi not found: ai:{ai != null}, is marked:{myGrid.MarkedForClose}, has root:{EntityToMasterAi.ContainsKey(myGrid)}"));

            return true;
        }

        private bool ServerTerminalMonitor(PacketObj data)
        {
            var packet = data.Packet;
            var terminalMonPacket = (TerminalMonitorPacket)packet;
            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>();

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            if (terminalMonPacket.State == TerminalMonitorPacket.Change.Update)
                TerminalMon.ServerUpdate(comp);
            else if (terminalMonPacket.State == TerminalMonitorPacket.Change.Clean)
                TerminalMon.ServerClean(comp);

            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerFixedWeaponHitEvent(PacketObj data)
        {
            var packet = data.Packet;
            var hitPacket = (FixedWeaponHitPacket)packet;

            var ent = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            var comp = ent?.Components.Get<CoreComponent>();

            if (comp?.Ai == null || comp.Platform.State != CorePlatform.PlatformState.Ready) return Error(data, Msg("BaseComp", comp != null), Msg("Ai", comp?.Ai != null), Msg("Ai", comp?.Platform.State == CorePlatform.PlatformState.Ready));

            var collection = comp.TypeSpecific != CoreComponent.CompTypeSpecific.Phantom ? comp.Platform.Weapons : comp.Platform.Phantoms;
            var weapon = collection[hitPacket.WeaponId];
            var targetEnt = MyEntities.GetEntityByIdOrDefault(hitPacket.HitEnt);

            if (targetEnt == null) return Error(data, Msg("TargetEnt"));

            var origin = targetEnt.PositionComp.WorldMatrixRef.Translation - hitPacket.HitOffset;
            var direction = hitPacket.Velocity;
            direction.Normalize();

            Projectiles.NewProjectiles.Add(new NewProjectile
            {
                AmmoDef = weapon.System.AmmoTypes[hitPacket.AmmoIndex].AmmoDef,
                Muzzle = weapon.Muzzles[hitPacket.MuzzleId],
                TargetEnt = targetEnt,
                Origin = origin,
                OriginUp = hitPacket.Up,
                Direction = direction,
                Velocity = hitPacket.Velocity,
                MaxTrajectory = hitPacket.MaxTrajectory,
                Type = NewProjectile.Kind.Client
            });

            data.Report.PacketValid = true;
            return true;
        }

        private bool ServerRequestMouseStates(PacketObj data)
        {
            var packet = data.Packet;
            var mouseUpdatePacket = new MouseInputSyncPacket
            {
                EntityId = -1,
                SenderId = packet.SenderId,
                PType = PacketType.FullMouseUpdate,
                Data = new PlayerMouseData[PlayerMouseStates.Count],
            };

            var c = 0;
            foreach (var playerMouse in PlayerMouseStates)
            {
                mouseUpdatePacket.Data[c] = new PlayerMouseData
                {
                    PlayerId = playerMouse.Key,
                    MouseStateData = playerMouse.Value
                };
            }

            if (PlayerMouseStates.Count > 0)
                PacketsToClient.Add(new PacketInfo
                {
                    Entity = null,
                    Packet = mouseUpdatePacket,
                    SingleClient = true,
                });

            data.Report.PacketValid = true;

            return true;
        }

        private bool ServerRequestReport(PacketObj data)
        {
            var packet = data.Packet;

            var entity = MyEntities.GetEntityByIdOrDefault(packet.EntityId);
            if (entity == null) return Error(data, Msg("Cube"));

            var reportData = ProblemRep.PullData(entity);
            if (reportData == null) return Error(data, Msg("RequestReport"));

            ProblemRep.NetworkTransfer(false, packet.SenderId, reportData);
            data.Report.PacketValid = true;

            return true;
        }
    }
}
