using System.Threading.Tasks;

namespace ET.Client
{
    [EntitySystemOf(typeof(ClientSenderCompnent))]
    [FriendOf(typeof(ClientSenderCompnent))]
    public static partial class ClientSenderCompnentSystem
    {
        //当ClientSenderCompnent实例被创建时，Awake方法会被调用，但在这个例子中，Awake方法并没有做任何事情
        [EntitySystem]
        private static void Awake(this ClientSenderCompnent self)
        {

        }
        
        // 当ClientSenderCompnent实例被销毁时，Destroy方法会被调用，它会移除相关的Fiber
        [EntitySystem]
        private static void Destroy(this ClientSenderCompnent self)
        {
            self.RemoveFiberAsync().Coroutine();
        }

        // 移除与ClientSenderCompnent实例相关的Fiber
        private static async ETTask RemoveFiberAsync(this ClientSenderCompnent self)
        {
            if (self.fiberId == 0)
            {
                return;
            }

            int fiberId = self.fiberId;
            self.fiberId = 0;
            await FiberManager.Instance.Remove(fiberId);
        }

        // 使用指定的账号和密码进行登录，并返回玩家ID
        public static async ETTask<long> LoginAsync(this ClientSenderCompnent self, string account, string password)
        {
            // 创建一个新的Fiber，并保存其ID
            self.fiberId = await FiberManager.Instance.Create(SchedulerType.ThreadPool, 0, SceneType.NetClient, "");
            self.netClientActorId = new ActorId(self.Fiber().Process, self.fiberId);

            // 发送登录请求，并等待响应
            NetClient2Main_Login response = await self.Root().GetComponent<ProcessInnerSender>().Call(self.netClientActorId, new Main2NetClient_Login()
            {
                OwnerFiberId = self.Fiber().Id, Account = account, Password = password
            }) as NetClient2Main_Login;
            return response.PlayerId;
        }

        public static void Send(this ClientSenderCompnent self, IMessage message)
        {
            A2NetClient_Message a2NetClientMessage = A2NetClient_Message.Create();
            a2NetClientMessage.MessageObject = message;
            self.Root().GetComponent<ProcessInnerSender>().Send(self.netClientActorId, a2NetClientMessage);
        }

        // 发送一个请求，并等待响应
        public static async ETTask<IResponse> Call(this ClientSenderCompnent self, IRequest request, bool needException = true)
        {
            A2NetClient_Request a2NetClientRequest = A2NetClient_Request.Create();
            a2NetClientRequest.MessageObject = request;
            A2NetClient_Response a2NetClientResponse = await self.Root().GetComponent<ProcessInnerSender>().Call(self.netClientActorId, a2NetClientRequest) as A2NetClient_Response;
            IResponse response = a2NetClientResponse.MessageObject;
            //如果响应超时，或者需要抛出异常，并且响应中包含错误，那么抛出一个RpcException            
            if (response.Error == ErrorCore.ERR_MessageTimeout)
            {
                throw new RpcException(response.Error, $"Rpc error: request, 注意Actor消息超时，请注意查看是否死锁或者没有reply: {request}, response: {response}");
            }

            if (needException && ErrorCore.IsRpcNeedThrowException(response.Error))
            {
                throw new RpcException(response.Error, $"Rpc error: {request}, response: {response}");
            }
            return response;
        }

    }
}