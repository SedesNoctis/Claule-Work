using System;
using System.Collections.Generic;
using System.Text;
using FlightGene;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

public static class AvirIdleWatch
{
	public struct Entry
	{
		public int tick;

		public string text;
	}

	private const int Keep = 12;

	private const int LogEveryTicks = 2500;

	private static readonly Dictionary<int, List<Entry>> history = new Dictionary<int, List<Entry>>();

	private static readonly Dictionary<int, int> lastLogged = new Dictionary<int, int>();

	private static readonly Dictionary<int, string> lastPather = new Dictionary<int, string>();

	private static readonly Dictionary<int, int> freshPather = new Dictionary<int, int>();

	public static bool Watched(Pawn p)
	{
		if (p != null && p.Spawned && p.IsColonist && !p.Drafted)
		{
			return AvirBody.IsAvir(p);
		}
		return false;
	}

	public static void NotePather(Pawn p, string text)
	{
		lastPather[p.thingIDNumber] = text;
		freshPather[p.thingIDNumber] = Find.TickManager.TicksGame;
	}

	public static string PatherNow(Pawn p)
	{
		if (!freshPather.TryGetValue(p.thingIDNumber, out var value) || value != Find.TickManager.TicksGame || !lastPather.TryGetValue(p.thingIDNumber, out var value2))
		{
			return "";
		}
		return " | path " + value2;
	}

	public static void Note(Pawn p, string text, bool log)
	{
		int ticksGame = Find.TickManager.TicksGame;
		if (!history.TryGetValue(p.thingIDNumber, out var value))
		{
			value = (history[p.thingIDNumber] = new List<Entry>());
		}
		value.Add(new Entry
		{
			tick = ticksGame,
			text = text
		});
		if (value.Count > 12)
		{
			value.RemoveAt(0);
		}
		if (log && (!lastLogged.TryGetValue(p.thingIDNumber, out var value2) || ticksGame - value2 >= 2500))
		{
			lastLogged[p.thingIDNumber] = ticksGame;
			Log.Message("[Avir idle] " + p.LabelShort + " stopped: " + text);
		}
	}

	public static string Describe(Pawn p, Job job)
	{
		if (job == null)
		{
			return "no job";
		}
		StringBuilder stringBuilder = new StringBuilder(job.def?.defName ?? "?");
		if (job.workGiverDef != null)
		{
			stringBuilder.Append(" (work giver " + job.workGiverDef.defName + ")");
		}
		else if (job.jobGiver != null)
		{
			stringBuilder.Append(" (from " + job.jobGiver.GetType().Name + ")");
		}
		if (job.targetA.IsValid)
		{
			stringBuilder.Append(" A=" + Target(job.targetA));
		}
		if (job.targetB.IsValid)
		{
			stringBuilder.Append(" B=" + Target(job.targetB));
		}
		stringBuilder.Append(" at " + p.Position.ToString());
		return stringBuilder.ToString();
	}

	private static string Target(LocalTargetInfo t)
	{
		if (!t.HasThing)
		{
			return t.Cell.ToString();
		}
		return t.Thing.LabelShort + "@" + t.Thing.PositionHeld.ToString();
	}

	public static string FlightState(Pawn p)
	{
		try
		{
			return "flying " + AvirFlightUtility.IsFlying(p) + ", can take off " + AvirFlightUtility.CanTakeOff(p) + ", engine Flying " + p.Flying;
		}
		catch (Exception ex)
		{
			return "flight state unreadable: " + ex.Message;
		}
	}

	public static string Report(Pawn p)
	{
		StringBuilder stringBuilder = new StringBuilder("[Avir idle] ==== " + p.LabelShort + " ====\n");
		stringBuilder.AppendLine("now: " + Describe(p, p.CurJob) + ", schedule " + p.timetable?.CurrentAssignment?.defName + ", " + FlightState(p));
		stringBuilder.AppendLine("roof here: " + (p.Map.roofGrid.RoofAt(p.Position)?.defName ?? "none") + ", standable here: " + p.Position.Standable(p.Map));
		if (lastPather.TryGetValue(p.thingIDNumber, out var value))
		{
			stringBuilder.AppendLine("last path failure: " + value);
		}
		if (history.TryGetValue(p.thingIDNumber, out var value2) && value2.Count > 0)
		{
			stringBuilder.AppendLine("-- failed jobs, oldest first --");
			int ticksGame = Find.TickManager.TicksGame;
			foreach (Entry item in value2)
			{
				stringBuilder.AppendLine("  " + (ticksGame - item.tick) + " ticks ago: " + item.text);
			}
		}
		else
		{
			stringBuilder.AppendLine("no failed jobs recorded since load");
		}
		try
		{
			ThinkResult thinkResult = p.thinker.MainThinkNodeRoot.TryIssueJobPackage(p, default(JobIssueParams));
			stringBuilder.AppendLine("think tree would give now: " + Describe(p, thinkResult.Job) + ((thinkResult.SourceNode != null) ? (" from " + thinkResult.SourceNode.GetType().Name) : ""));
		}
		catch (Exception ex)
		{
			stringBuilder.AppendLine("think tree threw: " + ex);
		}
		try
		{
			List<WorkGiver> list = p.workSettings?.WorkGiversInOrderNormal;
			if (list != null)
			{
				int num = 0;
				stringBuilder.AppendLine("-- first work givers with something to do --");
				foreach (WorkGiver item2 in list)
				{
					if (num >= 6)
					{
						break;
					}
					if (!(item2 is WorkGiver_Scanner workGiver_Scanner) || item2.ShouldSkip(p))
					{
						continue;
					}
					Job job = null;
					try
					{
						foreach (Thing item3 in workGiver_Scanner.PotentialWorkThingsGlobal(p) ?? Array.Empty<Thing>())
						{
							if (item3 != null && workGiver_Scanner.HasJobOnThing(p, item3))
							{
								job = workGiver_Scanner.JobOnThing(p, item3);
								if (job != null)
								{
									break;
								}
							}
						}
					}
					catch
					{
					}
					if (job != null)
					{
						num++;
						stringBuilder.AppendLine("  " + item2.def.defName + ": " + Describe(p, job) + ", reachable on foot " + p.CanReach(job.targetA, PathEndMode.Touch, Danger.Deadly));
					}
				}
			}
		}
		catch (Exception ex2)
		{
			stringBuilder.AppendLine("work giver scan threw: " + ex2.Message);
		}
		return stringBuilder.ToString();
	}

	[DebugAction("Avir", "Why is this pawn standing?", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
	private static void WhyStanding()
	{
		bool flag = false;
		foreach (object selectedObject in Find.Selector.SelectedObjects)
		{
			if (selectedObject is Pawn { Spawned: not false } pawn)
			{
				flag = true;
				Log.Message(Report(pawn));
			}
		}
		if (!flag)
		{
			Messages.Message("Select one or more pawns first.", MessageTypeDefOf.RejectInput, historical: false);
		}
	}
}
