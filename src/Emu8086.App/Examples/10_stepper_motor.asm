; Stepper motor on port 7: walk the half-step sequence to turn clockwise.
#start=stepper_motor.exe#
#make_COM#
org 100h

    mov bx, 0
step:
wait_ready:
    in al, 7                ; bit 7 = 1 when the motor is ready
    test al, 10000000b
    jz wait_ready

    mov al, sequence[bx]
    out 7, al
    inc bx
    cmp bx, 6
    jb step
    mov bx, 0
    jmp step

sequence db 001b, 011b, 010b, 110b, 100b, 101b
