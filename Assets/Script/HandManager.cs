using UnityEngine;

public class HandManger : MonoBehaviour
{
    public Vector3 handNormal;
    [SerializeField] GameObject left;
    [SerializeField] GameObject right;
    void Update()
    {
        handNormal = Vector3.Cross(Quaternion.Euler(left.transform.eulerAngles) * Vector3.forward, Quaternion.Euler(right.transform.eulerAngles) * Vector3.forward).normalized;
    }
}
