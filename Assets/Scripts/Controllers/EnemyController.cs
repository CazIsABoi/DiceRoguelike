using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyController : DiceSide
{
    protected override bool IsPlayerControlled => false;
    [SerializeField] private EnemyDefinition enemy;
    [SerializeField] private float throwForce = 10f;
    [SerializeField] private float sidewaysForce = 10f;
    [SerializeField] private float spinForce = 10f;
    public string DisplayName => enemy.displayName;
    private Coroutine throwRoutine;

    // Start: spawn enemy.dice, spawn slots from enemy.layout.pattern
    private void Start()
    {
        SpawnDice(enemy.dice);
        SpawnSlots(new List<FaceType>(enemy.layout.pattern));
        equationText.text = BuildEquation();
        StartTurn();
    }

    public void StartTurn()
    {
        Throw();
    }

    private IEnumerator ThrowRoutine()
    {
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            yield return new WaitForSeconds(Random.Range(.3f, 1.2f));

            spawnedDice[i].GetComponent<DiceController>().Throw(Vector3.up * throwForce + Random.insideUnitSphere * sidewaysForce, Random.onUnitSphere * spinForce);
        }
        // TODO: for each spawned die: 
        //   wait a random short time (feels like someone throwing)
        //   die.GetComponent<DiceController>().Throw(up * force + small random sideways, Random.onUnitSphere * spin)
    }

    private void Throw()
    {
        if (throwRoutine != null) StopCoroutine(throwRoutine);
        throwRoutine = StartCoroutine(ThrowRoutine());
    }
}