/***************************************************************************
 *   Hood Checker for Mac                                                  *
 *   Hood Checker © 2006 Andi8104, © 2007-2011 Mootilda                    *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 ***************************************************************************/

using System.Collections.Generic;
using SimPe.Interfaces.Files;

namespace LotExpander
{
    /// <summary>
    /// The callback surface the resource handlers (R_*) use to talk back to
    /// the run that drives them. In the original Windows tool this WAS
    /// <c>PrimaryForm</c>; extracting it to an interface lets the engine run
    /// headless and lets the Avalonia UI plug in later. Members mirror the
    /// exact <c>fParent.*</c> usage in the handlers.
    /// </summary>
    public interface IHoodCheckContext
    {
        /// <summary>The currently-open neighborhood package.</summary>
        IPackageFile NBPack { get; }

        /// <summary>Sim display name keyed by neighborhood instance.</summary>
        Dictionary<uint, string> SimNameFromInstance { get; }

        /// <summary>Sim neighborhood instance keyed by Sim ID.</summary>
        Dictionary<uint, uint> SimInstanceFromID { get; }

        /// <summary>When true, every memory is listed (not just problems).</summary>
        bool DisplayAllMemories { get; }

        /// <summary>Whether a Sim's relationship to itself is considered valid.</summary>
        bool SelfRelationsValid { get; }

        /// <summary>Append a line to the results/report.</summary>
        void AddToList(string s);

        /// <summary>Advance the progress indicator by one unit of work.</summary>
        void MadeProgress();

        /// <summary>Grow the total expected work by <paramref name="i"/> units.</summary>
        void IncreaseWorkload(int i);

        /// <summary>
        /// Decide which lot to use as the graveyard for a neighborhood that has
        /// invalid urnstones (only reached on the Remove/fix path). Replaces the
        /// original WinForms SelectGraveyardDialog + retry MessageBox: the UI (or
        /// a headless policy) presents <paramref name="potentialLots"/> and
        /// returns a <see cref="GraveyardChoice"/>. A null return is treated the
        /// same as "keep invalid urnstones for this hood".
        /// </summary>
        GraveyardChoice ResolveGraveyard(uint hoodId, List<R_DESC> potentialLots);
    }

    /// <summary>Outcome of a graveyard-selection prompt.</summary>
    public sealed class GraveyardChoice
    {
        /// <summary>Chosen graveyard lot, or null if none was selected.</summary>
        public R_DESC? SelectedLot;

        /// <summary>Apply <see cref="SelectedLot"/> to every subhood, not just this one.</summary>
        public bool UseForAll;

        /// <summary>User opted to remove ALL invalid urnstones (destructive) instead of choosing a lot.</summary>
        public bool RemoveAllInvalid;

        /// <summary>Non-destructive default: keep invalid urnstones for this hood.</summary>
        public static GraveyardChoice Keep() => new GraveyardChoice();
    }
}
