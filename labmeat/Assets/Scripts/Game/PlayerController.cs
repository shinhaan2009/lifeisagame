// 1인칭 이동: WASD, 마우스 시점, Shift 달리기. UI가 열려 있으면 입력을 받지 않는다.
using UnityEngine;

namespace LabMeat.Game
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public Camera cam;
        public float walk = 4f, run = 7f, sensitivity = 2.2f;
        CharacterController cc;
        float pitch, vy;

        public static PlayerController Create()
        {
            var go = new GameObject("Player");
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
            var p = go.AddComponent<PlayerController>();
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.62f, 0);
            p.cam = camGo.AddComponent<Camera>();
            p.cam.nearClipPlane = 0.05f;
            p.cam.fieldOfView = 70f;
            p.cam.clearFlags = CameraClearFlags.SolidColor;
            p.cam.backgroundColor = Color.white;
            camGo.AddComponent<AudioListener>();
            return p;
        }

        void Awake() { cc = GetComponent<CharacterController>(); }

        public void Teleport(Vector3 pos, float yaw)
        {
            cc.enabled = false;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            pitch = 0; vy = 0;
            if (cam != null) cam.transform.localRotation = Quaternion.identity;
            cc.enabled = true;
        }

        void Update()
        {
            var gm = GameManager.I;
            bool locked = gm == null || gm.InputLocked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
            Vector3 move = Vector3.zero;
            if (!locked)
            {
                float yaw = Input.GetAxis("Mouse X") * sensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, -85f, 85f);
                transform.Rotate(0, yaw, 0);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                float h = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                float v = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
                move = (transform.right * h + transform.forward * v);
                if (move.sqrMagnitude > 1) move.Normalize();
                move *= Input.GetKey(KeyCode.LeftShift) ? run : walk;
            }
            vy = cc.isGrounded ? -1f : vy - 18f * Time.deltaTime;
            move.y = vy;
            cc.Move(move * Time.deltaTime);
        }
    }
}
