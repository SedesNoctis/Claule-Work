using System.Collections.Generic;
using Avir;
using RimWorld;
using UnityEngine;
using Verse;

namespace AvirKharassianCompat
{
    public class CompProperties_ControlSphere : CompProperties
    {
        public CompProperties_ControlSphere()
        {
            compClass = typeof(CompControlSphere);
        }
    }

    /// <summary>
    /// The sphere's buttons, shown on whoever wears it: bind an instrument, release one, and -
    /// for a hungry Avir - drink through the sphere now rather than waiting for the hunger line.
    /// The bindings themselves live in GameComponent_SlaveControl, so they survive the sphere
    /// changing hands only as far as the rules there allow (taking it off breaks them all).
    /// </summary>
    [StaticConstructorOnStartup]
    public class CompControlSphere : ThingComp
    {
        private static readonly Texture2D BindIcon = ContentFinder<Texture2D>.Get("Avir/SlaveControl/Avir_Instrument");
        private static readonly Texture2D SphereIcon = ContentFinder<Texture2D>.Get("Avir/SlaveControl/Avir_ControlSphere");

        private Pawn Wearer => (parent as Apparel)?.Wearer;

        private static GameComponent_SlaveControl Control => Current.Game?.GetComponent<GameComponent_SlaveControl>();

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetWornGizmosExtra())
            {
                yield return g;
            }

            Pawn holder = Wearer;
            GameComponent_SlaveControl control = Control;
            if (holder == null || control == null || holder.Faction == null || !holder.Faction.IsPlayer)
            {
                yield break;
            }

            List<Pawn> bound = control.BoundTo(holder);

            Command_Action bind = new Command_Action
            {
                defaultLabel = "AvirSphereBind".Translate() + " (" + bound.Count + "/" + InstrumentUtility.MaxBoundPerSphere + ")",
                defaultDesc = "AvirSphereBindDesc".Translate(),
                icon = BindIcon,
                action = () => OpenBindMenu(holder, control)
            };
            if (!InstrumentUtility.CanGripSphere(holder))
            {
                bind.Disable("AvirSphereNoGrip".Translate(holder.LabelShortCap));
            }
            else if (bound.Count >= InstrumentUtility.MaxBoundPerSphere)
            {
                bind.Disable("AvirSphereFull".Translate(InstrumentUtility.MaxBoundPerSphere));
            }
            yield return bind;

            if (bound.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AvirSphereRelease".Translate(),
                    defaultDesc = "AvirSphereReleaseDesc".Translate(),
                    icon = SphereIcon,
                    action = () => OpenReleaseMenu(holder, control)
                };
            }

            Gene_ArcaneHunger hunger = ArcaneHungerCache.For(holder);
            if (hunger != null && bound.Count > 0)
            {
                Command_Action feed = new Command_Action
                {
                    defaultLabel = "AvirSphereFeed".Translate(),
                    defaultDesc = "AvirSphereFeedDesc".Translate(),
                    icon = SphereIcon,
                    action = () => control.FeedNow(holder)
                };
                if (!InstrumentUtility.SphereActive(holder))
                {
                    feed.Disable("AvirSphereNoGrip".Translate(holder.LabelShortCap));
                }
                else if (hunger.Value >= hunger.Max - 0.001f)
                {
                    feed.Disable("AvirSphereNotHungry".Translate(holder.LabelShortCap));
                }
                else if (!control.AnyFeedable(holder))
                {
                    feed.Disable("AvirSphereNoneToFeed".Translate());
                }
                yield return feed;
            }
        }

        private static void OpenBindMenu(Pawn holder, GameComponent_SlaveControl control)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (holder.Map != null)
            {
                List<Pawn> slaves = holder.Map.mapPawns.SlavesOfColonySpawned;
                for (int i = 0; i < slaves.Count; i++)
                {
                    Pawn slave = slaves[i];
                    if (!InstrumentUtility.IsInstrument(slave))
                    {
                        continue;
                    }
                    if (control.CanBind(holder, slave, out string why))
                    {
                        options.Add(new FloatMenuOption(slave.LabelShortCap, () => control.Bind(holder, slave)));
                    }
                    else
                    {
                        options.Add(new FloatMenuOption(slave.LabelShortCap + " (" + why + ")", null));
                    }
                }
            }
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("AvirSphereNoInstruments".Translate(), null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenReleaseMenu(Pawn holder, GameComponent_SlaveControl control)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<Pawn> bound = control.BoundTo(holder);
            for (int i = 0; i < bound.Count; i++)
            {
                Pawn slave = bound[i];
                options.Add(new FloatMenuOption(slave.LabelShortCap, () => control.Unbind(slave)));
            }
            if (options.Count > 1)
            {
                options.Add(new FloatMenuOption("AvirSphereReleaseAll".Translate(), () =>
                {
                    for (int i = 0; i < bound.Count; i++)
                    {
                        control.Unbind(bound[i]);
                    }
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override string CompInspectStringExtra()
        {
            Pawn holder = Wearer;
            GameComponent_SlaveControl control = Control;
            if (holder == null || control == null)
            {
                return null;
            }
            List<Pawn> bound = control.BoundTo(holder);
            string line = "AvirSphereInspect".Translate(bound.Count, InstrumentUtility.MaxBoundPerSphere);
            if (!InstrumentUtility.CanGripSphere(holder))
            {
                line += "\n" + "AvirSphereNoGrip".Translate(holder.LabelShortCap);
            }
            return line;
        }
    }
}
