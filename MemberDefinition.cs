using System;
using System.Reflection;
using Horizon;

namespace Horizon.Forge
{
    static class MemberDefinition
    {
        public static IMemberDefinition Field(string name, Type type) => new FieldMemberDefinition(name, type);

        public static IMemberDefinition Property(string name, Type type, bool isVirtual = true, MethodInfo raisePropertyChanged = null) => new PropertyMemberDefinition(name, type, isVirtual, raisePropertyChanged);

        public static IMemberDefinition Event(string name, Type type) => new EventMemberDefinition(name, type);

        public static IMemberDefinition Method(string name, Delegate @delegate, bool isVirtual = true) => new MethodMemberDefinition(name, @delegate, isVirtual);

        public static IMemberDefinition EmptyMethod(string name, Type returnType, bool isVirtual = true) => new EmptyMethodMemberDefinition(name, returnType, isVirtual);

        public static IMemberDefinition EmptyMethod(string name, Type returnType, Type[] parameterTypes, bool isVirtual = true) => new EmptyMethodMemberDefinition(name, returnType, isVirtual, parameterTypes);

        internal static IMemberDefinition EmptyMethod(IMethodCaller caller, bool isVirtual = true) => new EmptyMethodMemberDefinition(caller, isVirtual);

        public static IMemberDefinition Property(IPropertyCaller caller, bool isVirtual = true, MethodInfo raisePropertyChanged = null) => Property(caller.Name, caller.MemberType, isVirtual, raisePropertyChanged);
    }

    abstract class MemberDefinition<T> : IMemberDefinition
    {
        internal const MethodAttributes PublicProperty = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName;
        internal const MethodAttributes VirtPublicProperty = PublicProperty | MethodAttributes.Virtual;

        public bool IsVirtual { get; }
        public string Name { get; }
        public Type MemberType { get; }

        protected MemberDefinition(string name, Type type, bool isVirtual)
        {
            Name = name;
            MemberType = type;
            IsVirtual = isVirtual;
        }

        internal abstract T Build(System.Reflection.Emit.TypeBuilder typeBuilder);
        
        void IMemberDefinition.Build(System.Reflection.Emit.TypeBuilder typeBuilder)
        {
            Build(typeBuilder);
        }
    }
}
