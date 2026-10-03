#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// Hold to talk's one voice for the menu's columns (ADR 0021, ADR 0026), engine-free, for the menu's
    /// director to run: what the voice says and hears goes only to the column that held, which learns its
    /// hold started only once the voice records. A hold while the voice still records, or waits for the
    /// computer's answer, starts nothing, so one column's words never land in another's draft; only the
    /// hold that started a recording ends it. The column that held leaving the plane, or the app losing
    /// focus, drops what the voice records or awaits.
    /// </summary>
    public sealed class MenuVoice
    {
        private readonly Func<bool> busy;
        private readonly Action begin;
        private readonly Action send;
        private readonly Action drop;
        private (IMenuColumn Column, string Id)? held;

        /// <param name="busy">The voice records, or waits for the computer's answer.</param>
        /// <param name="begin">Starts recording where it may, and says why not where it can't.</param>
        /// <param name="send">Ends the recording and sends it to be heard.</param>
        /// <param name="drop">Drops a recording, or an answer still to come.</param>
        public MenuVoice(Func<bool> busy, Action begin, Action send, Action drop)
        {
            this.busy = busy;
            this.begin = begin;
            this.send = send;
            this.drop = drop;
        }

        /// <summary>The column the voice's words go to: the one that held last, until it leaves the plane.</summary>
        public IMenuColumn? Speaking { get; private set; }

        /// <summary>
        /// <paramref name="column"/>'s prompt <paramref name="id"/> was held: the voice starts recording
        /// for it, unless it is still busy with another hold's words. True when it records.
        /// </summary>
        public bool Hold(IMenuColumn column, string id)
        {
            if (busy()) return false;
            // What the voice says as it starts, as why it can't, is this column's.
            Speaking = column;
            begin();
            if (!busy()) return false;
            held = (column, id);
            column.HoldStarted(id);
            return true;
        }

        /// <summary>
        /// A hold on <paramref name="column"/>'s prompt <paramref name="id"/> ended: only the hold that
        /// started the recording ends it, let go sending what was said to be heard, else dropping it.
        /// </summary>
        public void Ended(IMenuColumn? column, string id, bool letGo)
        {
            if (!(held is (IMenuColumn holder, string started)) || holder != column || started != id) return;
            held = null;
            holder.HoldEnded(id, letGo);
            if (letGo) send();
            else drop();
        }

        /// <summary>What the computer heard, for the column that held.</summary>
        public void Heard(string text) => Speaking?.Heard(text);

        /// <summary>The voice's own line, listening or why nothing came of it, for the column that held.</summary>
        public void Said(string words) => Speaking?.Said(words);

        /// <summary><paramref name="column"/> left the plane: if the voice is its, what it records or awaits is dropped, and it hears no more.</summary>
        public void Left(IMenuColumn column)
        {
            if (Speaking != column) return;
            Stop();
            Speaking = null;
        }

        /// <summary>The app lost focus: what the voice records or awaits is dropped, a hold in progress ending as dropped.</summary>
        public void FocusLeft() => Stop();

        private void Stop()
        {
            if (held is (IMenuColumn holder, string id))
            {
                held = null;
                holder.HoldEnded(id, false);
            }
            drop();
        }
    }
}
