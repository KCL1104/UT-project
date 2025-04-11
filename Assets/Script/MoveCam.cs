using UnityEngine;

public class MoveCam : MonoBehaviour
{
    [SerializeField] Transform camPosition;
    void Update()
    {
        transform.position = camPosition.position;
    }
}
