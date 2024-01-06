using System;
using System.Net;

namespace ET.Client
{
    [FriendOf(typeof(RouterConnector))]
    [EntitySystemOf(typeof(RouterConnector))]
    public static partial class RouterConnectorSystem
    {
        [EntitySystem]
        private static void Awake(this RouterConnector self)
        {
            NetComponent netComponent = self.GetParent<NetComponent>();
            KService kService = (KService)netComponent.AService;
            kService.AddRouterAckCallback(self.Id, (flag) =>
            {
                self.Flag = flag;
            });
        }
        [EntitySystem]
        private static void Destroy(this RouterConnector self)
        {
            NetComponent netComponent = self.GetParent<NetComponent>();
            KService kService = (KService)netComponent.AService;
            kService.RemoveRouterAckCallback(self.Id);
        }

        // 这是一个 RouterConnector 类的扩展方法，它接受四个参数：一个字节数组（要发送的数据）、一个整数（数据的起始位置）、一个整数（数据的长度）和一个 IPEndPoint 对象（目标 IP 端点）。
        public static void Connect(this RouterConnector self, byte[] bytes, int index, int length, IPEndPoint ipEndPoint)
        {
            // 从 RouterConnector 对象中获取其父对象，这个父对象是一个 NetComponent 对象。
            NetComponent netComponent = self.GetParent<NetComponent>();

            // 从 NetComponent 对象中获取 AService 属性，然后将其转换为 KService 类型。
            KService kService = (KService)netComponent.AService;

            // 调用 KService 对象的 Transport 属性的 Send 方法，将字节数组中的数据发送到指定的 IP 端点。
            kService.Transport.Send(bytes, index, length, ipEndPoint);
        }
    }
}