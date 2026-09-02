using System;
using System.Collections.Generic;

namespace Sample
{
    public struct Point { public int X; public int Y; }

    public class Box<T> where T : struct
    {
        public T Value;
        public List<T> Many = new List<T>();
        public T Get(int i, string name) => Many[i];
    }

    public static class Arithmetic
    {
        public static int AddThenDouble(int a, int b)
        {
            var sum = a + b;
            return sum * 2;
        }
    }
}
