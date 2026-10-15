// Fair dice: two invisible rules that stop the worst bad-luck streaks on YOUR dice. Enemy dice are never touched.
// They only apply at High tables (at the other tables a low number or a missing × can be what you want).
//
// - × streak: a die with a × on it that has missed × this many throws in a row lands on × next time.
//   The count carries over between rounds, so it isn't a × every round.
// - Low repeat: a number die that just landed on a 1 or 2 (Low Max) can't land on a 1 or 2 on its next throw.
//
// How a throw is "fixed": a click throw (the slam) simply picks an allowed side. A drag throw rolls with real
// physics; if it settles on a side the rules don't allow, it hops (the same slam, smaller) onto an allowed side.
// That doesn't cost a throw.
//
// The numbers are set on the GameController (Fair Dice header). 0 turns a rule off.
public static class FairDice
{
    public static int XMissLimit = 8;   // misses in a row before the next throw is a ×. 0 = off
    public static int LowMax = 2;       // 1s and 2s can't come up twice in a row on a number die. 0 = off

    public static bool AppliesAt(TableType table) => table == TableType.High;

    public static bool IsMultiply(FaceDefinition face) => face != null && face.type == FaceType.Operator && face.op == Operator.Multiply;
    public static bool IsLow(FaceDefinition face) => LowMax > 0 && face != null && face.type == FaceType.Number && face.number <= LowMax;
}
