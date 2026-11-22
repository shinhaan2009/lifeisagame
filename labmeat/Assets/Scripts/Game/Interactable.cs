// 상호작용 가능한 시설 표식. MonoBehaviour는 파일 이름과 클래스 이름이 같아야 하므로 따로 둔다.
using UnityEngine;

namespace LabMeat.Game
{
    public enum Kind { Reactor, Bench, Fridge, Computer, Bed, Parcel, Door, Taxi, Restaurant, Investor, Candidate }

    public class Interactable : MonoBehaviour
    {
        public Kind kind;
        public string location = "garage";   // 리액터·작업대·냉장고·컴퓨터가 속한 장소
        public int index;                    // 리액터 슬롯, 지인 집 번호
        public string label;
    }
}
