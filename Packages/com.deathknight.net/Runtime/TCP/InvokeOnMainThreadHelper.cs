using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DeathKnight.Net
{
    public class InvokeOnMainThreadHelper
    {
        public InvokeOnMainThreadHelper()
        {
            mainThreadContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException(
                    "MyTcpClient must be created on the Unity main thread.");

            mainThreadId = Environment.CurrentManagedThreadId;
        }

        private SynchronizationContext mainThreadContext;
        private int mainThreadId;
        public void InvokeOnMainThreadPost(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId == mainThreadId)
            {
                InvokeSafely(callback);
                return;
            }

            mainThreadContext.Post(
                state => InvokeSafely((Action)state),
                callback);
        }

        public void InvokeOnMainThreadPost<T>(Action<T> callback, T argument)
        {
            if (callback == null)
            {
                return;
            }

            InvokeOnMainThreadPost(() => callback(argument));
        }

        public void InvokeOnMainThreadPost<T, U>(Action<T, U> callback, T argument, U argument2)
        {
            if (callback == null)
            {
                return;
            }

            InvokeOnMainThreadPost(() => callback(argument, argument2));
        }


        public void InvokeSafely(Action callback)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public void InvokeOnMainThreadSend(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId == mainThreadId)
            {
                InvokeSafely(callback);
                return;
            }

            mainThreadContext.Send(
                state => InvokeSafely((Action)state),
                callback);
        }

        public void InvokeOnMainThreadSend<T>(Action<T> callback, T argument)
        {
            if (callback == null)
            {
                return;
            }

            InvokeOnMainThreadSend(() => callback(argument));
        }

        public void InvokeOnMainThreadSend<T, U>(Action<T, U> callback, T argument, U argument2)
        {
            if (callback == null)
            {
                return;
            }

            InvokeOnMainThreadSend(() => callback(argument, argument2));
        }
    }
}
