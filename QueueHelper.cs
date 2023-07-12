//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Collections.Concurrent;
//using SanTint.Opc.Server.Model;

//namespace SanTint.Opc.Server
//{
//    public class QueueHelper
//    {
//        //public static BlockingCollection<ADUReceived> ADUReceivedBlockingCollection = new BlockingCollection<ADUReceived>();
//        static List<ADUSent> ADUSents = new List<ADUSent>();
//        static List<ADUReceived> ADUReceiveds = new List<ADUReceived>();
//        //定义两个锁对象,分别用于ADUSents和ADUReceiveds
//        static object ADUSentsLock = new object();
//        static object ADUReceivedsLock = new object();

//        public static void AddADUSent(ADUSent aduSent)
//        {
//            lock (ADUSentsLock)
//            {
//                ADUSents.Add(aduSent);
//            }
//        }

//        public static void RemoveADUSent(ADUSent aduSent)
//        {
//            lock (ADUSentsLock)
//            {
//                ADUSents.Remove(aduSent);
//            }
//        }

//        public static ADUSent TryPickUpADUSent()
//        {
//            lock (ADUSentsLock)
//            {
//                if (ADUSents.Count > 0)
//                {
//                    var aduSent = ADUSents[0];
//                    ADUSents.RemoveAt(0);
//                    return aduSent;
//                }
//                else
//                {
//                    return null;
//                }
//            }
//        }

//        public static void AddADUReceived(ADUReceived aduReceived)
//        {
//            lock (ADUReceivedsLock)
//            {
//                ADUReceiveds.Add(aduReceived);
//            }
//        }

//        public static void RemoveADUReceived(ADUReceived aduReceived)
//        {
//            lock (ADUReceivedsLock)
//            {
//                ADUReceiveds.Remove(aduReceived);
//            }
//        }

//        public static ADUReceived TryPickUpADUReceived()
//        {
//            lock (ADUReceivedsLock)
//            {
//                if (ADUReceiveds.Count > 0)
//                {
//                    var aduReceived = ADUReceiveds[0];
//                    ADUReceiveds.RemoveAt(0);
//                    return aduReceived;
//                }
//                else
//                {
//                    return null;
//                }
//            }
//        }
//    }
//}
