; Robot: walk forward, turn right at walls and switch on every lamp in front of it.
; Port 9 command, port 10 examine result, port 11 status.
#start=robot.exe#
#make_COM#
org 100h

main:
    mov al, 4               ; examine the cell in front
    out 9, al
wait_data:
    in al, 11
    test al, 1              ; bit 0: data ready
    jz wait_data
    in al, 10

    cmp al, 0               ; empty -> move forward
    je forward
    cmp al, 8               ; switched-off lamp -> switch it on
    je lamp_on
    mov al, 3               ; wall or lit lamp -> turn right
    out 9, al
    jmp main
forward:
    mov al, 1
    out 9, al
    jmp main
lamp_on:
    mov al, 5
    out 9, al
    mov al, 3
    out 9, al
    jmp main
