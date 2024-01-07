namespace ET.Client
{
    public static class LoginHelper
    {
        public static async ETTask Login(Scene root, string account, string password)
        {
            root.RemoveComponent<ClientSenderCompnent>();
            ClientSenderCompnent clientSenderCompnent = root.AddComponent<ClientSenderCompnent>();

            var response = await clientSenderCompnent.LoginAsync(account, password);
            if (response.Error != ErrorCode.ERR_Success)
            {
                
                root.RemoveComponent<ClientSenderCompnent>();
                Log.Error("登录失败，错误码：" + response.Error.ToString());
                return;
            }

            root.GetComponent<PlayerComponent>().MyId = response.PlayerId;
            
            await EventSystem.Instance.PublishAsync(root, new LoginFinish());
        }
    }
}