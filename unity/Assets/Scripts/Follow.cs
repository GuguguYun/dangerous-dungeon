using UnityEngine;

/// Keeps an effect centred on a target (ambient particles around the player).
public class Follow : MonoBehaviour
{
    public Transform target;

    void LateUpdate()
    {
        if (target) transform.position = new Vector3(target.position.x, 0f, target.position.z);
    }
}
