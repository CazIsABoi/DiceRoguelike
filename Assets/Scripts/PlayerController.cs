using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField] private Transform[] numSlots;
    private Die[] numSlotsDice;
    [SerializeField] private Transform[] opSlots;
    private Die[] opSlotsDice;
    [SerializeField] private DiceSlot[] slots;
    private List<FaceDefinition> opFaces = new List<FaceDefinition>();
    private List<FaceDefinition> numFaces = new List<FaceDefinition>();
    [SerializeField] private float moveTime = 0.25f;
    private int diceInSlots = 0;

    [Header("Dice")]
    [SerializeField] private Die diePrefab; // Prefab
    [SerializeField] private DieDefinition[] die;
    private Die[] spawnedDice;
    [SerializeField] private Transform spawnPoint; // Where dice spawn
    [SerializeField] private Transform ground;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;
    private int Score;

    private void Start()
    {
       spawnedDice = new Die[die.Length];
       numSlotsDice = new Die[numSlots.Length];
       opSlotsDice = new Die[opSlots.Length];

       SpawnDice();

       scoreText.text = "Value: 0";
    }

    public void SpawnDice()
    {
        for (int i = 0; i < die.Length; i++)
        {
            Die newDie = Instantiate(diePrefab, spawnPoint.position, UnityEngine.Random.rotation);
            spawnedDice[i] = newDie;
            spawnedDice[i].Initialize(die[i]);

            DiceController controller = newDie.GetComponent<DiceController>();
            controller.Setup(ground, spawnPoint);

            for (int j = 0; j < die[i].faceDefinitions.Length; j++)
            {
                if (die[i].faceDefinitions[j] == null) continue;
                FaceDefinition currentFace = die[i].faceDefinitions[j];
                if (currentFace.type == FaceType.Operator)
                    opFaces.Add(currentFace);
                else
                    numFaces.Add(currentFace);
            }
        }
        for (int i = 0; i < numFaces.Count; i++)
        {
            print(numFaces[i].type);
        }
        for (int i = 0; i < opFaces.Count; i++)
        {
            print(opFaces[i].type);
        }
    }
    public void GetTopFacesInSlots()
    {
        // Get left numbers
        print("Amount of slots: " + (numSlots.Length + opSlots.Length) + " with " + numSlots.Length + " num slots and " + opSlots.Length + " operation slots");
        int numbers;
        string operators;

        for (int i = 0; i < numSlots.Length; i++)
        {

        }
    }

    /*
    private FaceDefinition DiceSlot(int index)
    {
    }
    */

    public void MoveDiceToSlot(Die dice)
    {
        Rigidbody rb = dice.GetComponent<Rigidbody>();
        rb.isKinematic = true;

        bool isNumber = dice.GetTopFace().type == FaceType.Number;
        Transform[] slots = isNumber ? numSlots : opSlots;
        Die[] slotDice = isNumber ? numSlotsDice : opSlotsDice;

        bool placed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slotDice[i] != null) continue;
            StartCoroutine(MoveRoutine(dice.transform, slots[i]));
            slotDice[i] = dice;
            placed = true;
            break;
        }

        if (!placed)
        {
            rb.isKinematic = false; // No free slot, let it stay physical
            return;
        }

        diceInSlots++;
        if (diceInSlots == numSlots.Length + opSlots.Length)
        {
            GetTopFacesInSlots();
        }
    }

    private IEnumerator MoveRoutine(Transform dice, Transform slot)
    {
        Vector3 startPos = dice.position;
        Quaternion startRot = dice.rotation;
        float elapsed = 0f;

        Vector3 localUp = ClosestLocalAxis(dice, Vector3.up);
        Vector3 localForward = ClosestLocalAxis(dice, slot.forward);
        Quaternion targetRot = slot.rotation * Quaternion.Inverse(Quaternion.LookRotation(localForward, localUp));
        print(localUp);

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            dice.position = Vector3.Lerp(startPos, slot.position, t);
            dice.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        dice.position = slot.position;
        dice.rotation = targetRot;
    }
    private static readonly Vector3[] axes =
    {
        Vector3.up, Vector3.down, Vector3.right,
        Vector3.left, Vector3.forward, Vector3.back
    };

    private Vector3 ClosestLocalAxis(Transform t, Vector3 worldDir)
    {
        Vector3 best = axes[0];
        float bestDot = -Mathf.Infinity;

        foreach (Vector3 axis in axes)
        {
            Vector3 worldAxis = t.TransformDirection(axis);
            float dot = Vector3.Dot(worldAxis, worldDir);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = axis;
            }
        }

        return best;
    }
}
