// How a round is scored. Set per enemy on its EnemyDefinition.
//   High:   the bigger result wins, damage = the difference (the normal game)
//   Low:    the result closer to 0 wins, damage = the difference in distance x the stake (a distance counts Low Cap at most)
//   Target: the result closer to that round's target wins, damage = the difference in distance
public enum TableType { High, Low, Target }