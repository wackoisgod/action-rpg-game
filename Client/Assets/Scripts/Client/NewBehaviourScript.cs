using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        LibCommon.FooBar.Thing("This");
        LibCommon.WaffleHouse.Thing("thisd is a test");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
