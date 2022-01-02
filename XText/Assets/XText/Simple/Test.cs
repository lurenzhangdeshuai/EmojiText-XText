using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XT;

public class Test : MonoBehaviour
{
    // Start is called before the first frame update

    public XText xText;
    void Start()
    {
        xText.text = "[E1]滴滴滴[u%这是一个下划线]";
        xText.SetHyperData(1, (s) =>
        {
            Debug.Log("----自定义点击事件------自定义参数："+s);
        },"自定义参数666","1");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
