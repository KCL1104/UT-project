using System;
using System.Collections;
using UnityEngine;


public class Volleyball : MonoBehaviour
{
    [SerializeField] GameObject spawn;
    [SerializeField] GameObject player;
    [SerializeField] HandManger hand;
    [SerializeField] float hitPower;
    [SerializeField] float speed;
    [SerializeField] float distance;
    [SerializeField] float height;
    [SerializeField] float wait;
    Rigidbody rb;
    bool airborne;

    private void Start() 
    {
        rb = GetComponent<Rigidbody>();
        callReset();
    }
    private void OnCollisionEnter(Collision other) 
    {
        if(airborne && other.gameObject.tag == "Respawn")
        {
            airborne = false;
            Invoke("callReset", 2);
        }
    }

    private void OnTriggerEnter(Collider other) 
    {
        if(other.gameObject.tag == "Hand")
        {
            Vector3 vel = Vector3.Reflect(rb.velocity, hand.handNormal).normalized * hitPower;
            vel.y = Math.Abs(vel.y);
            vel.z = -vel.z;
            rb.velocity = vel;
        }       
    }

    private void callReset() {
        StartCoroutine(Reset());
    }

    private IEnumerator Reset()
    {
        airborne = true;
        transform.position = spawn.transform.position + new Vector3(UnityEngine.Random.Range(-4f,4f), height, 0);
        rb.velocity = Vector3.zero;
        transform.rotation = Quaternion.identity;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;
        yield return new WaitForSeconds(wait);
        rb.useGravity = true;
        if((player.transform.position - transform.position).magnitude > distance)
        {
            rb.AddForce((player.transform.position - transform.position).normalized * (speed * 2), ForceMode.Impulse);
        }
        else
        {
            rb.AddForce((player.transform.position - transform.position).normalized * speed, ForceMode.Impulse);
        }     
    }
}
