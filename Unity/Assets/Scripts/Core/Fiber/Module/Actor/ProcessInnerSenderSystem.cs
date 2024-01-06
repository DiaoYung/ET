using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The system responsible for handling the inner sending of messages to actors within the ET framework.
/// </summary>
namespace ET
{
    [EntitySystemOf(typeof(ProcessInnerSender))]
    [FriendOf(typeof(ProcessInnerSender))]
    public static partial class ProcessInnerSenderSystem
    {
        // Destroy方法用于销毁当前的ProcessInnerSender，它会从消息队列中移除当前的Fiber
        [EntitySystem]
        private static void Destroy(this ProcessInnerSender self)
        {
            // 获取当前的Fiber
            Fiber fiber = self.Fiber();
            // 从消息队列中移除当前的Fiber
            MessageQueue.Instance.RemoveQueue(fiber.Id);
        }

        // Awake方法用于唤醒当前的ProcessInnerSender，它会在消息队列中添加当前的Fiber
        [EntitySystem]
        private static void Awake(this ProcessInnerSender self)
        {
            // 获取当前的Fiber
            Fiber fiber = self.Fiber();
            // 在消息队列中添加当前的Fiber
            MessageQueue.Instance.AddQueue(fiber.Id);
        }

        // Update方法用于更新当前的ProcessInnerSender，它会从消息队列中获取消息，并处理这些消息
        [EntitySystem]
        private static void Update(this ProcessInnerSender self)
        {
            // 清空当前的消息列表
            self.list.Clear();
            // 获取当前的Fiber
            Fiber fiber = self.Fiber();
            // 从消息队列中获取消息，最多获取1000条
            MessageQueue.Instance.Fetch(fiber.Id, 1000, self.list);

            // 遍历获取到的消息
            foreach (MessageInfo actorMessageInfo in self.list)
            {
                // 处理每一条消息
                self.HandleMessage(fiber, actorMessageInfo);
            }
        }

        // HandleMessage方法用于处理消息，如果消息是响应，它会调用HandleIActorResponse方法处理响应
        // 否则，它会将消息添加到对应的邮箱中
        private static void HandleMessage(this ProcessInnerSender self, Fiber fiber, in MessageInfo messageInfo)
        {
            // 如果消息是响应
            if (messageInfo.MessageObject is IResponse response)
            {
                // 处理响应
                self.HandleIActorResponse(response);
                return;
            }

            // 获取消息的ActorId和MessageObject
            ActorId actorId = messageInfo.ActorId;
            MessageObject message = messageInfo.MessageObject;

            // 获取对应的邮箱
            MailBoxComponent mailBoxComponent = self.Fiber().Mailboxes.Get(actorId.InstanceId);
            // 如果邮箱不存在
            if (mailBoxComponent == null)
            {
                // 打印警告日志
                Log.Warning($"actor not found mailbox, from: {actorId} current: {fiber.Address} {message}");
                // 如果消息是请求
                if (message is IRequest request)
                {
                    // 创建一个表示Actor未找到的响应，并回复这个响应
                    IResponse resp = MessageHelper.CreateResponse(request, ErrorCore.ERR_NotFoundActor);
                    self.Reply(actorId.Address, resp);
                }
                // 释放消息
                message.Dispose();
                return;
            }
            // 将消息添加到邮箱中
            mailBoxComponent.Add(actorId.Address, message);
        }

        // HandleIActorResponse方法用于处理响应，它会从回调映射中移除对应的请求，并运行响应
        private static void HandleIActorResponse(this ProcessInnerSender self, IResponse response)
        {
            // 如果回调映射中存在对应的请求
            if (!self.requestCallback.Remove(response.RpcId, out MessageSenderStruct actorMessageSender))
            {
                return;
            }
            // 运行响应
            Run(actorMessageSender, response);
        }
        
        // Run方法处理响应结果。如果响应表示超时错误，它会在任务完成源上设置一个异常。
        // 如果响应表示错误并且启用了异常，它会在任务完成源上设置一个异常。
        // 否则，它会将任务完成源的结果设置为响应。
        private static void Run(MessageSenderStruct self, IResponse response)
        {
            // 如果响应表示超时错误
            if (response.Error == ErrorCore.ERR_MessageTimeout)
            {
                // 在任务完成源上设置一个表示超时的异常
                self.Tcs.SetException(new RpcException(response.Error, $"Rpc error: request, 注意Actor消息超时，请注意查看是否死锁或者没有reply: actorId: {self.ActorId} {self.Request}, response: {response}"));
                return;
            }

            // 如果响应表示错误并且启用了异常
            if (self.NeedException && ErrorCore.IsRpcNeedThrowException(response.Error))
            {
                // 在任务完成源上设置一个表示错误的异常
                self.Tcs.SetException(new RpcException(response.Error, $"Rpc error: actorId: {self.ActorId} request: {self.Request}, response: {response}"));
                return;
            }

            // 将任务完成源的结果设置为响应
            self.Tcs.SetResult(response);
        }

        // 发送消息的方法，将消息转换为MessageObject类型后，调用SendInner方法进行发送
        public static void Reply(this ProcessInnerSender self, Address fromAddress, IResponse message)
        {
            self.SendInner(new ActorId(fromAddress, 0), (MessageObject)message);
        }

        public static void Send(this ProcessInnerSender self, ActorId actorId, IMessage message)
        {
            self.SendInner(actorId, (MessageObject)message);
        }

        // SendInner方法是实际执行消息发送的方法。如果Actor在不同的进程中，它会抛出一个异常。
        // 如果Actor在同一个Fiber中，它会直接处理消息。否则，它会将消息发送到消息队列。
        private static void SendInner(this ProcessInnerSender self, ActorId actorId, MessageObject message)
        {
            // 获取当前的Fiber
            Fiber fiber = self.Fiber();
            
            // 如果消息的目标Actor和当前的Fiber不在同一个进程中，抛出异常
            // 如果发向同一个进程，则扔到消息队列中
            if (actorId.Process != fiber.Process)
            {
                throw new Exception($"actor inner process diff: {actorId.Process} {fiber.Process}");
            }
            // 如果消息的目标Actor和当前的Fiber在同一个Fiber中，直接处理这个消息
            if (actorId.Fiber == fiber.Id)
            {
                self.HandleMessage(fiber, new MessageInfo() {ActorId = actorId, MessageObject = message});
                return;
            }
            // 如果消息的目标Actor和当前的Fiber不在同一个Fiber中，但在同一个进程中，将消息发送到消息队列中            
            MessageQueue.Instance.Send(fiber.Address, actorId, message);
        }

        // 获取下一个RpcId的方法，每次调用都会自增RpcId
        public static int GetRpcId(this ProcessInnerSender self)
        {
            return ++self.RpcId;
        }

        // 发送请求的方法，这是一个异步方法，返回一个ETTask<IResponse>，表示等待响应的任务
        public static async ETTask<IResponse> Call(
                this ProcessInnerSender self,
                ActorId actorId,
                IRequest request,
                bool needException = true
        )
        {
            // 为请求分配一个RpcId
            request.RpcId = self.GetRpcId();

            // 如果ActorId是默认值，抛出异常
            if (actorId == default)
            {
                throw new Exception($"actor id is 0: {request}");
            }

            // 调用Call方法发送请求，并等待响应
            return await self.Call(actorId, request.RpcId, request, needException);
        }
        
        /// <summary>
        /// Call方法用于发送请求并等待响应
        /// </summary>
        /// <param name="self"></param>
        /// <param name="actorId">目标Actor的ID</param>
        /// <param name="rpcId">请求的RpcId</param>
        /// <param name="iRequest">请求对象</param>
        /// <param name="needException">如果为true，当请求超时或者出错时会抛出异常，否则会返回一个表示错误的响应</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async ETTask<IResponse> Call(
                this ProcessInnerSender self,
                ActorId actorId,
                int rpcId,
                IRequest iRequest,
                bool needException = true
        )
        {
            // 如果ActorId是默认值，抛出异常
            if (actorId == default)
            {
                throw new Exception($"actor id is 0: {iRequest}");
            }
            // 获取当前的Fiber
            Fiber fiber = self.Fiber();
            // 如果消息的目标Actor和当前的Fiber不在同一个进程中，抛出异常
            // 如果发向同一个进程，则扔到消息队列中
            if (fiber.Process != actorId.Process)
            {
                throw new Exception($"actor inner process diff: {actorId.Process} {fiber.Process}");
            }
            
            // 创建一个任务完成源，用于等待响应
            var tcs = ETTask<IResponse>.Create(true);

            // 将请求添加到回调映射中
            self.requestCallback.Add(rpcId, new MessageSenderStruct(actorId, iRequest, tcs, needException));
            
            // 发送请求
            self.SendInner(actorId, (MessageObject)iRequest);

            // 定义一个超时方法
            async ETTask Timeout()
            {
                // 等待一段时间
                await fiber.Root.GetComponent<TimerComponent>().WaitAsync(ProcessInnerSender.TIMEOUT_TIME);

                // 如果回调映射中不存在对应的请求，直接返回
                if (!self.requestCallback.Remove(rpcId, out MessageSenderStruct action))
                {
                    return;
                }
                
                // 如果需要异常
                if (needException)
                {
                    // 在任务完成源上设置一个表示超时的异常
                    action.Tcs.SetException(new Exception($"actor sender timeout: {iRequest}"));
                }
                else
                {
                    // 创建一个表示超时的响应，并在任务完成源上设置这个响应
                    IResponse response = MessageHelper.CreateResponse(iRequest, ErrorCore.ERR_Timeout);
                    action.Tcs.SetResult(response);
                }
            }
            
            // 启动超时方法
            Timeout().Coroutine();
            
            // 记录开始时间
            long beginTime = TimeInfo.Instance.ServerFrameTime();

            // 等待响应
            IResponse response = await tcs;
            
            // 记录结束时间
            long endTime = TimeInfo.Instance.ServerFrameTime();

            // 计算耗时
            long costTime = endTime - beginTime;
            // 如果耗时超过200毫秒，打印警告日志
            if (costTime > 200)
            {
                Log.Warning($"actor rpc time > 200: {costTime} {iRequest}");
            }
            
            // 返回响应
            return response;
        }
    }
}