using UnityEngine;
using UnityEngine.AI; // NavMeshAgent를 쓰려면 필요
using Game.Scripts.AI; 

public class ClickToMove : MonoBehaviour
{
    public HybridAgentController player;
    
    // [추가] 애니메이터 변수
    private Animator anim;
    private NavMeshAgent agent;

    void Start()
    {
        // Player 안에 있는 NavMeshAgent 찾기
        agent = player.GetComponent<NavMeshAgent>();
        
        // [중요] Visual 자식 오브젝트에 있는 Animator 찾기
        // Player(부모) 아래에 Visual(자식)이 있으므로 GetComponentInChildren 사용
        anim = player.GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // 1. 이동 명령 (기존 코드)
        if (Input.GetMouseButtonDown(0)) // Both 설정이라 작동 함
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                player.SetDestination(hit.point);
            }
        }

        // 2. [추가] 애니메이션 동기화
        if (anim != null && agent != null)
        {
            // 현재 이동 속도를 애니메이터의 "Speed" 파라미터에 전달
            anim.SetFloat("Speed", agent.velocity.magnitude);
        }
    }
}