using UnityEngine;
using UnityEngine.UI;

namespace ET.Client
{
    /// <summary>
    /// 客户端增加两个程序集 ModelView跟HotfixView，目前客户端共有5个程序集，其作用如下
    /// 1.Model 逻辑层数据结构定义
    /// 2.Hotfix 逻辑层的逻辑方法
    /// 3.ModelView 显示层的数据结构定义
    /// 4.HotfixView 显示层的逻辑方法
    /// 5.ThirdParty 第三方库
    /// 为什么要分出ModelView跟HotfixView呢？主要原因是要分离显示层跟逻辑层，逻辑层的代码其实可以用来做压测机器人。如果一开始就定好这样的结构，压测机器人完全可以利用客户端逻辑层的代码，节省大量时间
    /// </summary>
    [EntitySystemOf(typeof(UILobbyComponent))]
    [FriendOf(typeof(UILobbyComponent))]
    public static partial class UILobbyComponentSystem
    {
        [EntitySystem]
        private static void Awake(this UILobbyComponent self)
        {
            ReferenceCollector rc = self.GetParent<UI>().GameObject.GetComponent<ReferenceCollector>();

            self.enterMap = rc.Get<GameObject>("EnterMap");
            self.enterMap.GetComponent<Button>().onClick.AddListener(() => { self.EnterMap().Coroutine(); });
        }
        
        public static async ETTask EnterMap(this UILobbyComponent self)
        {
            Scene root = self.Root();
            await EnterMapHelper.EnterMapAsync(root);
            await UIHelper.Remove(root, UIType.UILobby);
        }
    }
}