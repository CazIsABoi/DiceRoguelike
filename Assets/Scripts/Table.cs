using UnityEngine;

public class Table : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        print("WEEWOO WEEWOO");
       if (other.CompareTag("Dice") && other.GetComponent<Rigidbody>().isKinematic == false) {
            other.GetComponent<DiceController>().Respawn();
        }
    }
}
