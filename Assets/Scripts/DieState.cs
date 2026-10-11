// Every state a die can be in (guide section 2).
//
// One enum instead of five bools (isDragging, rolling, hasLanded, puttingBack...). With bools, "dragging AND
// rolling AND locked" can happen by accident, and every bug hunt starts with "which bools are true right now?".
// With one enum a die is in exactly one state, and Debug.Log(State) tells you which.
public enum DieState
{
    Hovering,   // floating at its spawn spot, spinning: ready to grab, no face yet
    Held,       // in your hand
    Rolling,    // thrown, not settled yet
    Resting,    // landed with a face up: lock it in, or throw again if it has throws left
    Locked,     // in a slot
    Busted      // no slot for its face
}
