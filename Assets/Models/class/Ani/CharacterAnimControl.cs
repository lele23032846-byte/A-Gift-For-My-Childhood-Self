using UnityEngine;

public class CharacterAnimControl : MonoBehaviour
{
    private Animator _animator;
    private CharacterController _controller;

    void Start()
    {
        _animator = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // 获取玩家输入
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        Vector3 moveDir = new Vector3(horizontal, 0, vertical);
        float speed = moveDir.magnitude;

        // 设置Speed参数控制Idle/Walk/Run切换
        _animator.SetFloat("Speed", speed);

        // 按空格键触发跳跃动画
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _animator.SetTrigger("Jump");
        }

        // 按鼠标左键触发攻击动画
        bool isAttacking = Input.GetMouseButton(0);
        _animator.SetBool("IsAttacking", isAttacking);
    }
}