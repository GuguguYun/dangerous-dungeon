using UnityEngine;

/// Spins slowly and fires Entered once when the player steps into it.
public class Portal : MonoBehaviour
{
    public PlayerController player;
    public System.Action Entered;
    bool used;

    void Update()
    {
        transform.Rotate(0f, 40f * Time.deltaTime, 0f);
        if (used || !player) return;
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude < 1.3f)
        {
            used = true;
            Entered?.Invoke();
        }
    }
}
