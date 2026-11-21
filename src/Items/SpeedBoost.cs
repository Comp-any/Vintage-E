using System;

namespace VintageE
{
    /// <summary>
    /// Swiftwolf's speed on the game's move speed multiplier. That number isn't Vintage-E's alone: the game puts it
    /// back to 1 when a player's game mode changes to survival, an admin's "/player ... movespeed" sets it, and another
    /// mod's flight or potion may. Until 1.25.2 the ring wrote "what it was at switch-on × the ring's speed" back
    /// every half second and put the old number back at switch-off, so whatever anyone else had set in between was
    /// undone, twice.
    ///
    /// Now it remembers what it last wrote, and looks at what is there:
    /// - its own number (or within the rounding the game sends it to players with): carries on as before;
    /// - a lower number: something set the speed anew (the game's reset, an admin). That becomes the speed the ring
    ///   multiplies;
    /// - a higher number: something has added its own speed on top of the ring's. It is left alone, and the ring
    ///   carries on when its own number is back;
    /// - a lower number three ticks in a row: something keeps writing its own. The ring leaves the speed to it until
    ///   it is next switched on, instead of two writers flipping the number back and forth.
    /// At switch-off the speed goes back to what it was only if the number there is still the ring's.
    /// </summary>
    public struct SpeedBoost
    {
        public const int MaxStreak = 3;
        /// <summary>Nothing to write when the multiplier is this close to what's wanted.</summary>
        const float Eps = 0.001f;
        /// <summary>
        /// A number this close to the ring's is the ring's: the game sends the multiplier to players in 64ths, and
        /// writes a player's rounded copy back when they change their fly mode or view (admins only).
        /// </summary>
        public const float Same = 0.05f;

        /// <summary>The multiplier without the ring's speed; null while the ring's speed is off.</summary>
        public float? Base;
        /// <summary>What the ring last made the multiplier.</summary>
        public float Set;
        /// <summary>How many ticks in a row something else had lowered it.</summary>
        public int Streak;
        /// <summary>Something else is setting the multiplier: left to it for now.</summary>
        public bool Yielded;

        /// <summary>The multiplier to set now for this <paramref name="factor"/> (1 = off), or null to leave it as it is.</summary>
        public float? Next(float factor, float current)
        {
            if (factor > 1 + Eps)
            {
                if (Base == null) { Base = current; Streak = 0; Yielded = false; }
                else if (Math.Abs(current - Set) > Same)
                {
                    if (current > Set) { Yielded = true; return null; }
                    if (Streak < MaxStreak) Streak++;
                    if (Streak >= MaxStreak) { Yielded = true; return null; }
                    Base = current;
                    Yielded = false;
                }
                else { Streak = 0; Yielded = false; }
                float want = Base.Value * factor;
                Set = want;
                return Math.Abs(current - want) > Eps ? want : (float?)null;
            }
            if (Base == null) return null;
            // Still what the ring made it: back to what it was. Changed by something else since: left alone.
            float? back = Math.Abs(current - Set) <= Same && Math.Abs(current - Base.Value) > Eps ? Base : null;
            Base = null; Streak = 0; Yielded = false; Set = 0;
            return back;
        }
    }
}
