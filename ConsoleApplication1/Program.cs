using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConsoleApplication1
{
    interface I
    {
        void Do();
    }

    class A : I
    {
        public void Do()
        {
            System.Diagnostics.Debug.WriteLine("A");
        }
    }

    class B : I
    {
        public void Do()
        {
            System.Diagnostics.Debug.WriteLine("B");
        }
    }

    class Program
    {
        static void Do(I i)
        {

        }
        static void Do(A a)
        {
            a.Do();
        }
        static void Do(B b)
        {
            b.Do();
        }
        static void Main(string[] args)
        {
            I a = new A();
            I b = new B();
            Do(a);
            Do(b);
        }
    }
}
