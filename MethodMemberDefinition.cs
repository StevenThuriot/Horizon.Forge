using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Horizon.Forge
{
    class MethodMemberDefinition : MemberDefinition<MethodBuilder>
    {
        internal const MethodAttributes _defaultMethodAttributes = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
        readonly MethodInfo _methodInfo;

        public MethodMemberDefinition(string name, Delegate @delegate, bool isVirtual)
            : this(name, @delegate.Method, isVirtual)
        {
        }

        public MethodMemberDefinition(string name, MethodInfo methodInfo, bool isVirtual)
            : base(name, methodInfo.ReturnType, isVirtual)
        {
            _methodInfo = methodInfo;
        }

        public MethodMemberDefinition(MethodInfo methodInfo, bool isVirtual)
            : base(methodInfo.Name, methodInfo.ReturnType, isVirtual)
        {
            _methodInfo = methodInfo;
        }

        internal override MethodBuilder Build(System.Reflection.Emit.TypeBuilder typeBuilder)
        {
            var name = Name;
            
            var parameterTypes = _methodInfo.GetParameters().Select(x => x.ParameterType).ToArray();

            var methodAttributes = _defaultMethodAttributes;

            if (IsVirtual)
                methodAttributes |= MethodAttributes.Virtual;

            var methodBuilder = typeBuilder.DefineMethod(name, methodAttributes, _methodInfo.ReturnType, parameterTypes);

            var generator = methodBuilder.GetILGenerator();

            for (var i = 1; i <= parameterTypes.Length; i++)
                generator.Emit(OpCodes.Ldarg_S, i);

            generator.Emit(OpCodes.Call, _methodInfo);
            generator.Emit(OpCodes.Ret);

            return methodBuilder;
        }
    }
}