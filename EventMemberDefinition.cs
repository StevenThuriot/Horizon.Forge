using System;
using System.Reflection;
using System.Reflection.Emit;
using Horizon;

namespace Horizon.Forge
{
    class EventMemberDefinition : MemberDefinition<EventBuilder>
    {
        static readonly MethodInfo _delegateCombine;
        static readonly MethodInfo _delegateRemove;

        static EventMemberDefinition()
        {
            var delegateType = typeof (Delegate);
            var arguments = new[] {delegateType, delegateType};

            _delegateCombine = Info<Delegate>.GetSpecificMethod("Combine", arguments).MethodInfo;
            _delegateRemove = Info<Delegate>.GetSpecificMethod("Remove", arguments).MethodInfo;
        }

        public EventMemberDefinition(string name, Type type)
            : base(name, type, false)
        {
        }

        internal override EventBuilder Build(System.Reflection.Emit.TypeBuilder typeBuilder)
        {
            var eventName = Name;
            var eventHandlerType = MemberType;

            var eventHandlerTypes = new[] {eventHandlerType};

            var eventBackingField = typeBuilder.DefineField(eventName, eventHandlerType, FieldAttributes.Private);

            var voidType = typeof (void);

            //Combine event
            var add = typeBuilder.DefineMethod("add_" + eventName,
                                               VirtPublicProperty |
                                               MethodAttributes.Final |
                                               MethodAttributes.NewSlot,
                                               voidType,
                                               eventHandlerTypes);

            var generator = add.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, eventBackingField);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Call, _delegateCombine);
            generator.Emit(OpCodes.Castclass, eventHandlerType);
            generator.Emit(OpCodes.Stfld, eventBackingField);
            generator.Emit(OpCodes.Ret);

            //Remove event
            var remove = typeBuilder.DefineMethod("remove_" + eventName,
                                                  VirtPublicProperty |
                                                  MethodAttributes.Final |
                                                  MethodAttributes.NewSlot,
                                                  voidType,
                                                  eventHandlerTypes);

            generator = remove.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, eventBackingField);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Call, _delegateRemove);
            generator.Emit(OpCodes.Castclass, eventHandlerType);
            generator.Emit(OpCodes.Stfld, eventBackingField);
            generator.Emit(OpCodes.Ret);


            //event
            var eventBuilder = typeBuilder.DefineEvent(eventName, EventAttributes.None, eventHandlerType);
            eventBuilder.SetAddOnMethod(add);
            eventBuilder.SetRemoveOnMethod(remove);

            return eventBuilder;
        }
    }
}