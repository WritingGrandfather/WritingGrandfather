using UnityEngine;

public class PlayerHp : MonoBehaviour
{
    public static PlayerHp Instance;
    private int hp;

    public int HP
    {
        get { return hp; }
        set
        {
            hp = value;

            if (hp <= 0)
            {
                Die();
            }
        }
    }
    
    public int maxHp;

    public void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        hp = maxHp;
    }

    public void Die()
    {
        
    }
}
