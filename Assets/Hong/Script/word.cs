using System;
using System.Collections;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

public class word : MonoBehaviour
{
    public string id;
    public int damage = 1;
    public int hp = 1;
    public int maxHp = 1;
    public int speed = 1;
    public GameObject ground;
    public TextMeshPro text;
    string[] words =
    {
        "새", "원숭이", "기린", "코끼리", "개", "고양이", "거북이", "염소",
        "가재", "여우", "곰", "호랑이", "토끼", "하마", "고래", "사자",
        "오리", "거울", "의자", "모자", "양말", "신발", "우산", "시계",
        "가방", "물", "우유", "헤드셋", "캔", "플라스틱", "키보드", "마우스",
        "모니터", "노트북", "안경", "시계", "농구", "배구", "축구",
        "바리스타", "성악", "기자", "뉴스", "그림", "학교", "폰",
        "프린터", "배터리", "야구", "골프", "탁구", "미술", "피아노",
        "집", "나무", "구름", "커피", "비누", "우주", "지구", "달",
        "사과", "바나나", "수박", "포도", "딸기", "당근", "버섯",
        "치즈", "빵", "고기", "밥"
    };
    public void Awake()
    {
        if (ground == null)
        {
            ground = GameObject.FindWithTag("Ground");
        }
        maxHp = hp;
    }

    public void OnEnable()
    {
        hp = maxHp;
        text.text = words[Random.Range(0, words.Length)];
    }

    public void Update()
    {
        transform.position += Vector3.down * (speed * Time.deltaTime);
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Ground"))
        {
            PlayerHp.Instance.HP -= damage;
            PoolManager.Instance.Release(id, gameObject);
        }
    }
}
