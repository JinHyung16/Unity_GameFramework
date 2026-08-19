namespace Game_UIFramework
{
    public interface IWindowUpdate
    {
        void OnUpdate(float deltaTime);
        void OnFixedUpdate(float fixedDeltaTime);
    }
}
