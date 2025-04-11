using UnityEngine;
using UnityEngine.UI;

public class setSpeedTxt : MonoBehaviour
{
    [SerializeField] Rigidbody rb;
    [SerializeField] Text text;
    void Update()
    {
        text.text = "Speed: " + rb.velocity.magnitude.ToString();
    }
}
