using UnityEngine;

public class SingleTonMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    private static readonly object lockObject = new();
    
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                lock (lockObject)
                {
                    if (instance == null)
                    {
                        // 在场景中查找已存在的实例
                        instance = FindObjectOfType<T>();
                        
                        // 如果没找到，创建新的 GameObject
                        if (instance == null)
                        {
                            GameObject singletonObject = new GameObject(typeof(T).Name);
                            instance = singletonObject.AddComponent<T>();
                        }
                    }
                }
            }
            return instance;
        }
    }
    
    protected virtual void Awake()
    {
        // 防止重复实例
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        instance = this as T;
    }
}
